using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// 提供多端文本同步、文本历史和临时文件中转能力。
/// </summary>
public sealed class CollaborativeEditorService
{
    public const long MaxUploadBytes = 100 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _textFilePath;
    private readonly string _uploadDirectory;
    private readonly ILogger<CollaborativeEditorService> _logger;
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private readonly object _stateLock = new();
    private readonly List<string> _history = [];
    private string _text = string.Empty;

    public CollaborativeEditorService(IWebHostEnvironment environment, ILogger<CollaborativeEditorService> logger)
    {
        _logger = logger;
        var dataDirectory = Path.Combine(environment.ContentRootPath, "Data", "collaborative-editor");
        _uploadDirectory = Path.Combine(dataDirectory, "uploads");
        _textFilePath = Path.Combine(dataDirectory, "content.txt");
        Directory.CreateDirectory(_uploadDirectory);

        if (File.Exists(_textFilePath))
        {
            _text = File.ReadAllText(_textFilePath, Encoding.UTF8);
            if (_text.Length > 0)
            {
                _history.Add(_text);
            }
        }
    }

    public CollaborativeEditorState GetState()
    {
        lock (_stateLock)
        {
            return new CollaborativeEditorState(_text, _history.ToArray(), GetFiles());
        }
    }

    public async Task UpdateTextAsync(string text, CancellationToken cancellationToken)
    {
        text ??= string.Empty;
        CollaborativeEditorState state;
        lock (_stateLock)
        {
            if (text == _text)
            {
                return;
            }

            _text = text;
            _history.Add(text);
            if (_history.Count > 1000)
            {
                _history.RemoveRange(0, _history.Count - 1000);
            }

            state = new CollaborativeEditorState(_text, _history.ToArray(), GetFiles());
        }

        await File.WriteAllTextAsync(_textFilePath, text, Encoding.UTF8, cancellationToken);
        await BroadcastAsync(new
        {
            type = "text_update",
            text = state.Text,
            history = state.History
        }, cancellationToken);
    }

    public async Task<CollaborativeUploadResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var fileName = ValidateFileName(file.FileName);
        if (file.Length > MaxUploadBytes)
        {
            throw new ArgumentException("单个文件不能超过 100 MB。", nameof(file));
        }

        var path = GetFilePath(fileName);
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await file.CopyToAsync(stream, cancellationToken);
        }
        catch (IOException)
        {
            throw new InvalidOperationException("同名文件已经存在，或文件无法写入。", new IOException(fileName));
        }

        await BroadcastFileListAsync(cancellationToken);
        return new CollaborativeUploadResponse(fileName, "文件上传成功。");
    }

    public string? FindFile(string fileName)
    {
        try
        {
            var safeName = ValidateFileName(fileName);
            var path = GetFilePath(safeName);
            return File.Exists(path) ? path : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        var safeName = ValidateFileName(fileName);
        var path = GetFilePath(safeName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        await BroadcastFileListAsync(cancellationToken);
        return true;
    }

    public async Task HandleWebSocketAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var client = new ClientConnection(socket);
        var clientId = Guid.NewGuid();
        _clients[clientId] = client;

        try
        {
            await client.SendAsync(new
            {
                type = "state",
                state = GetState()
            }, cancellationToken);

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(socket, cancellationToken);
                if (message is null)
                {
                    break;
                }

                await HandleMessageAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "连接已关闭", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    private async Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                return;
            }

            switch (typeProperty.GetString())
            {
                case "text_update":
                    await UpdateTextAsync(root.TryGetProperty("text", out var textProperty) ? textProperty.GetString() ?? string.Empty : string.Empty, cancellationToken);
                    break;
                case "get_current_state":
                    await BroadcastAsync(new { type = "state", state = GetState() }, cancellationToken);
                    break;
                case "file_list_update":
                    await BroadcastFileListAsync(cancellationToken);
                    break;
            }
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "忽略无法解析的协同编辑器消息");
        }
    }

    private async Task BroadcastFileListAsync(CancellationToken cancellationToken)
    {
        await BroadcastAsync(new { type = "file_list_update", files = GetFiles() }, cancellationToken);
    }

    private async Task BroadcastAsync(object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var sends = _clients.Select(async entry =>
        {
            try
            {
                await entry.Value.SendAsync(payload, cancellationToken);
            }
            catch (WebSocketException)
            {
                _clients.TryRemove(entry.Key, out _);
            }
        });
        await Task.WhenAll(sends);
    }

    private static async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        await using var content = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                await content.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(content.ToArray());
    }

    private IReadOnlyList<string> GetFiles()
    {
        return Directory.EnumerateFiles(_uploadDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string GetFilePath(string fileName) => Path.Combine(_uploadDirectory, fileName);

    private static string ValidateFileName(string? fileName)
    {
        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or ".." || safeName != fileName)
        {
            throw new ArgumentException("文件名无效。", nameof(fileName));
        }

        return safeName;
    }

    private sealed class ClientConnection
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly WebSocket _socket;

        public ClientConnection(WebSocket socket)
        {
            _socket = socket;
        }

        public async Task SendAsync(object message, CancellationToken cancellationToken)
        {
            await SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions), cancellationToken);
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
