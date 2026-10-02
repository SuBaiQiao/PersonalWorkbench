const cronForm = document.querySelector('#cronForm');
const cronFrequency = document.querySelector('#cronFrequency');
const cronInterval = document.querySelector('#cronInterval');
const cronHour = document.querySelector('#cronHour');
const cronMinute = document.querySelector('#cronMinute');
const cronWeekday = document.querySelector('#cronWeekday');
const cronDayOfMonth = document.querySelector('#cronDayOfMonth');
const cronIntervalField = document.querySelector('#cronIntervalField');
const cronIntervalHint = document.querySelector('#cronIntervalHint');
const cronHourField = document.querySelector('#cronHourField');
const cronMinuteField = document.querySelector('#cronMinuteField');
const cronWeekdayField = document.querySelector('#cronWeekdayField');
const cronDayOfMonthField = document.querySelector('#cronDayOfMonthField');
const generateCronButton = document.querySelector('#generateCronButton');
const clearCronButton = document.querySelector('#clearCronButton');
const copyCronButton = document.querySelector('#copyCronButton');
const cronStatus = document.querySelector('#cronStatus');
const cronExpression = document.querySelector('#cronExpression');
const cronDescription = document.querySelector('#cronDescription');

function setCronStatus(message, tone = '') {
    cronStatus.textContent = message;
    if (tone) {
        cronStatus.dataset.tone = tone;
    } else {
        delete cronStatus.dataset.tone;
    }
}

async function readCronError(response) {
    try {
        const payload = await response.json();
        return payload.message || '请求失败，请稍后重试。';
    } catch {
        return '请求失败，请稍后重试。';
    }
}

function updateCronFields() {
    const frequency = cronFrequency.value;
    const usesTime = frequency !== 'minutely';
    cronIntervalField.hidden = !['minutely', 'hourly'].includes(frequency);
    cronHourField.hidden = !usesTime;
    cronMinuteField.hidden = !usesTime;
    cronWeekdayField.hidden = frequency !== 'weekly';
    cronDayOfMonthField.hidden = frequency !== 'monthly';

    if (frequency === 'minutely') {
        cronInterval.max = '59';
        cronIntervalHint.textContent = '分钟，范围 1-59';
    } else if (frequency === 'hourly') {
        cronInterval.max = '23';
        cronIntervalHint.textContent = '小时，范围 1-23；从指定小时开始';
    }
}

function buildCronRequest() {
    return {
        frequency: cronFrequency.value,
        interval: Number(cronInterval.value),
        hour: Number(cronHour.value),
        minute: Number(cronMinute.value),
        dayOfWeek: Number(cronWeekday.value),
        dayOfMonth: Number(cronDayOfMonth.value)
    };
}

async function generateCron(event) {
    event.preventDefault();
    generateCronButton.disabled = true;
    setCronStatus('正在请求后端生成…');

    try {
        const response = await fetch('/api/cron/generate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(buildCronRequest())
        });

        if (!response.ok) {
            throw new Error(await readCronError(response));
        }

        const result = await response.json();
        cronExpression.textContent = result.expression;
        cronDescription.textContent = result.description;
        copyCronButton.disabled = !result.expression;
        setCronStatus('表达式生成完成。', 'success');
    } catch (error) {
        setCronStatus(error.message || '请求失败，请稍后重试。', 'error');
    } finally {
        generateCronButton.disabled = false;
    }
}

function resetCronForm() {
    cronFrequency.value = 'daily';
    cronInterval.value = '5';
    cronHour.value = '9';
    cronMinute.value = '0';
    cronWeekday.value = '1';
    cronDayOfMonth.value = '1';
    cronExpression.textContent = '尚未生成';
    cronDescription.textContent = '生成后会显示规则说明。';
    copyCronButton.disabled = true;
    setCronStatus('');
    updateCronFields();
}

async function copyCronExpression() {
    const expression = cronExpression.textContent;
    if (!expression || expression === '尚未生成') {
        return;
    }

    try {
        await navigator.clipboard.writeText(expression);
        setCronStatus('Cron 表达式已复制到剪贴板。', 'success');
    } catch {
        setCronStatus('复制失败，请检查浏览器剪贴板权限。', 'error');
    }
}

cronFrequency.addEventListener('change', updateCronFields);
cronForm.addEventListener('submit', generateCron);
clearCronButton.addEventListener('click', resetCronForm);
copyCronButton.addEventListener('click', copyCronExpression);
updateCronFields();
