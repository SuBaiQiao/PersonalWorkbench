using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class PdfExpenseMergeService
{
    /// <summary>
    /// 根据每个 PDF 第一页的高度分类并合并上传文件。
    /// </summary>
    /// <param name="inputs">当前请求中用户选择的 PDF 文件流，顺序代表用户选择顺序。</param>
    /// <returns>合并后的 PDF 二进制内容。</returns>
    /// <exception cref="ArgumentException">没有可处理的输入文件时抛出。</exception>
    /// <exception cref="InvalidDataException">输入文件不是有效 PDF 时抛出。</exception>
    public byte[] Merge(IReadOnlyList<PdfMergeInput> inputs)
    {
        if (inputs.Count == 0)
        {
            throw new ArgumentException("至少选择一个 PDF 文件。", nameof(inputs));
        }

        var regularDocuments = new List<PdfDocument>();
        var tallDocuments = new List<PdfDocument>();
        var openedDocuments = new List<PdfDocument>();

        try
        {
            foreach (var input in inputs)
            {
                if (input.Content.CanSeek)
                {
                    input.Content.Position = 0;
                }

                PdfDocument document;
                try
                {
                    document = PdfReader.Open(input.Content, PdfDocumentOpenMode.Import);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidDataException($"文件“{input.FileName}”不是有效的 PDF。", exception);
                }

                openedDocuments.Add(document);
                if (document.PageCount == 0)
                {
                    continue;
                }

                // 与 Python 脚本保持一致：第一页高度超过 600pt 的文件延后合并。
                var firstPage = document.Pages[0];
                if (firstPage.Height.Point > 600)
                {
                    tallDocuments.Add(document);
                }
                else
                {
                    regularDocuments.Add(document);
                }
            }

            using var mergedDocument = new PdfDocument();
            foreach (var document in regularDocuments.Concat(tallDocuments))
            {
                // 导入文档中的每一页，保持同一文件内部的页序不变。
                for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    mergedDocument.AddPage(document.Pages[pageIndex]);
                }
            }

            using var output = new MemoryStream();
            mergedDocument.Save(output, false);
            return output.ToArray();
        }
        finally
        {
            // 即使某个文件读取或合并失败，也要释放已经打开的 PDF 文档。
            foreach (var document in openedDocuments)
            {
                document.Close();
            }
        }
    }
}
