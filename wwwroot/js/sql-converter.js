const sqlForm = document.querySelector('#sqlForm');
const inputSql = document.querySelector('#inputSql');
const outputSql = document.querySelector('#outputSql');
const convertButton = document.querySelector('#convertButton');
const clearButton = document.querySelector('#clearButton');
const copySqlButton = document.querySelector('#copySqlButton');
const sqlStatus = document.querySelector('#sqlStatus');

function setSqlStatus(message, tone = '') {
    sqlStatus.textContent = message;
    if (tone) {
        sqlStatus.dataset.tone = tone;
    } else {
        delete sqlStatus.dataset.tone;
    }
}

async function readSqlError(response) {
    try {
        const payload = await response.json();
        return payload.message || '请求失败，请稍后重试。';
    } catch {
        return '请求失败，请稍后重试。';
    }
}

async function convertSql(event) {
    event.preventDefault();
    convertButton.disabled = true;
    setSqlStatus('正在请求后端转换…');

    try {
        const response = await fetch('/api/sql/convert', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ inputSql: inputSql.value })
        });

        if (!response.ok) {
            throw new Error(await readSqlError(response));
        }

        const result = await response.json();
        outputSql.value = result.outputSql;
        copySqlButton.disabled = !result.outputSql;
        setSqlStatus(result.outputSql ? '转换完成。' : '输入为空，未生成结果。', result.outputSql ? 'success' : '');
    } catch (error) {
        setSqlStatus(error.message || '请求失败，请稍后重试。', 'error');
    } finally {
        convertButton.disabled = false;
    }
}

function clearSql() {
    inputSql.value = '';
    outputSql.value = '';
    copySqlButton.disabled = true;
    setSqlStatus('');
    inputSql.focus();
}

async function copySql() {
    if (!outputSql.value) {
        return;
    }

    try {
        await navigator.clipboard.writeText(outputSql.value);
        setSqlStatus('结果已复制到剪贴板。', 'success');
    } catch {
        setSqlStatus('复制失败，请检查浏览器剪贴板权限。', 'error');
    }
}

sqlForm.addEventListener('submit', convertSql);
clearButton.addEventListener('click', clearSql);
copySqlButton.addEventListener('click', copySql);
