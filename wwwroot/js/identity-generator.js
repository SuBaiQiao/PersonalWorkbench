const identityForm = document.querySelector('#identityForm');
const provinceSelect = document.querySelector('#provinceSelect');
const citySelect = document.querySelector('#citySelect');
const countySelect = document.querySelector('#countySelect');
const birthDateInput = document.querySelector('#birthDate');
const genderSelect = document.querySelector('#gender');
const countInput = document.querySelector('#count');
const generateButton = document.querySelector('#generateButton');
const copyIdentityButton = document.querySelector('#copyIdentityButton');
const identityStatus = document.querySelector('#identityStatus');
const resultCount = document.querySelector('#resultCount');
const resultsBody = document.querySelector('#identityResults');

let generatedRows = [];
let areas = [];

function setStatus(message, tone = '') {
    identityStatus.textContent = message;
    if (tone) {
        identityStatus.dataset.tone = tone;
    } else {
        delete identityStatus.dataset.tone;
    }
}

function addOptions(select, items, emptyLabel) {
    select.replaceChildren(new Option(emptyLabel, ''));
    for (const item of items) {
        select.add(new Option(item.name, item.code));
    }
}

function selectedProvince() {
    return areas.find((area) => area.code === provinceSelect.value);
}

function selectedCity() {
    return selectedProvince()?.cities.find((city) => city.code === citySelect.value);
}

function handleProvinceChange() {
    const province = selectedProvince();
    addOptions(citySelect, province?.cities ?? [], '随机城市');
    citySelect.disabled = !province;
    addOptions(countySelect, [], '随机区县');
    countySelect.disabled = true;
}

function handleCityChange() {
    const city = selectedCity();
    addOptions(countySelect, city?.counties ?? [], '随机区县');
    countySelect.disabled = !city;
}

function renderRows(rows) {
    resultsBody.replaceChildren();
    resultCount.textContent = rows.length;
    copyIdentityButton.disabled = rows.length === 0;

    if (rows.length === 0) {
        const emptyRow = document.createElement('tr');
        emptyRow.className = 'empty-row';
        const emptyCell = document.createElement('td');
        emptyCell.colSpan = 5;
        emptyCell.textContent = '暂无生成结果';
        emptyRow.append(emptyCell);
        resultsBody.append(emptyRow);
        return;
    }

    for (const row of rows) {
        const tableRow = document.createElement('tr');
        const values = [row.name, row.idNumber, row.gender, row.birthDate, row.areaCode];
        values.forEach((value, index) => {
            const cell = document.createElement('td');
            cell.textContent = value;
            if (index === 1) {
                cell.className = 'mono';
            }
            tableRow.append(cell);
        });
        resultsBody.append(tableRow);
    }
}

async function readError(response) {
    try {
        const payload = await response.json();
        return payload.message || '请求失败，请检查输入后重试。';
    } catch {
        return '请求失败，请稍后重试。';
    }
}

async function generateIdentities(event) {
    event?.preventDefault();
    generateButton.disabled = true;
    setStatus('正在请求后端生成…');

    const payload = {
        provinceCode: provinceSelect.value || null,
        cityCode: citySelect.value || null,
        countyCode: countySelect.value || null,
        birthDate: birthDateInput.value || null,
        gender: genderSelect.value,
        count: Number(countInput.value)
    };

    try {
        const response = await fetch('/api/identity/generate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            throw new Error(await readError(response));
        }

        generatedRows = await response.json();
        renderRows(generatedRows);
        setStatus(`已生成 ${generatedRows.length} 条测试数据。`, 'success');
    } catch (error) {
        generatedRows = [];
        renderRows(generatedRows);
        setStatus(error.message || '请求失败，请稍后重试。', 'error');
    } finally {
        generateButton.disabled = false;
    }
}

async function copyIdentities() {
    if (generatedRows.length === 0) {
        return;
    }

    const text = generatedRows
        .map((row) => [row.name, row.idNumber, row.gender, row.birthDate, row.areaCode].join('\t'))
        .join('\n');

    try {
        await navigator.clipboard.writeText(text);
        setStatus('结果已复制到剪贴板。', 'success');
    } catch {
        setStatus('复制失败，请检查浏览器剪贴板权限。', 'error');
    }
}

async function initialize() {
    setStatus('正在加载地区数据…');

    try {
        const response = await fetch('/api/identity/areas');
        if (!response.ok) {
            throw new Error(await readError(response));
        }

        areas = await response.json();
        addOptions(provinceSelect, areas, '随机省份');
        await generateIdentities();
    } catch (error) {
        setStatus(error.message || '地区数据加载失败。', 'error');
    }
}

provinceSelect.addEventListener('change', handleProvinceChange);
citySelect.addEventListener('change', handleCityChange);
identityForm.addEventListener('submit', generateIdentities);
copyIdentityButton.addEventListener('click', copyIdentities);
initialize();
