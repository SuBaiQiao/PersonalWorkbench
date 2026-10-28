import test from 'node:test';
import assert from 'node:assert/strict';
import { inspectCron } from '../src/utils/cron.js';

const preview = (expression, options = {}) => inspectCron(expression, { start: Date.UTC(2026, 9, 27), count: 3, ...options });
const dates = result => result.occurrences.map(item => item.utc);

test('Quartz parses the requested daily expression and retains all six field labels', () => {
    const result = preview('  0  9 1 * * ?  ');
    assert.equal(result.expression, '0 9 1 * * ?');
    assert.equal(result.format, 'quartz');
    assert.deepEqual(result.fieldLabels, ['秒', '分钟', '小时', '日期', '月份', '星期']);
    assert.equal(result.dayOr, false);
    assert.match(result.description, /01:09/);
    assert.deepEqual(dates(result), ['2026-10-27T01:09:00.000Z', '2026-10-28T01:09:00.000Z', '2026-10-29T01:09:00.000Z']);
});

test('a single expression schedules both requested hours in either format', () => {
    const expected = ['2026-10-27T01:09:00.000Z', '2026-10-27T12:09:00.000Z', '2026-10-28T01:09:00.000Z'];
    assert.deepEqual(dates(preview('0 9 1,12 * * ?')), expected);
    assert.deepEqual(dates(preview('9 1,12 * * *')), expected);
    assert.match(preview('0 9 1,12 * * ?').description, /01:09.*12:09/);
});

test('Quartz honors nonzero seconds, time zones and an exclusive start instant', () => {
    assert.deepEqual(dates(preview('30 9 1,12 * * ?', {
        zone: 'Asia/Shanghai', start: Date.UTC(2026, 9, 26, 17, 9, 30)
    })), ['2026-10-27T04:09:30.000Z', '2026-10-27T17:09:30.000Z', '2026-10-28T04:09:30.000Z']);
    assert.deepEqual(dates(preview('*/20 * * * * ?')), [
        '2026-10-27T00:00:20.000Z', '2026-10-27T00:00:40.000Z', '2026-10-27T00:01:00.000Z'
    ]);
});

test('Quartz weekday numbering is Sunday=1 and Saturday=7; Unix stays unchanged', () => {
    for (const [quartz, unix] of [['1', '0'], ['7', '6'], ['2-6', 'MON-FRI'], ['2,4,6', 'MON,WED,FRI'], ['MON-FRI/2', 'MON,WED,FRI'], ['*/2', '0,2,4,6']]) {
        const result = preview(`0 0 9 ? * ${quartz}`);
        assert.deepEqual(dates(result), dates(preview(`0 9 * * ${unix}`)));
    }
    assert.match(preview('0 0 9 ? * 1').description, /星期日/);
    assert.match(preview('0 0 9 ? * 7').description, /星期六/);
});

test('Quartz supports monthly dates, named months, ranges and steps', () => {
    assert.equal(preview('0 0 0 29 FEB ?', { start: Date.UTC(2025, 0, 1), count: 1 }).occurrences[0].utc, '2028-02-29T00:00:00.000Z');
    assert.deepEqual(dates(preview('15 9 1-12/11 * * ?')), ['2026-10-27T01:09:15.000Z', '2026-10-27T12:09:15.000Z', '2026-10-28T01:09:15.000Z']);
});

test('Quartz rejects malformed questions, unsupported extensions and invalid values', () => {
    for (const expression of [
        '0 9 1 * * *', '0 9 1 ? * ?', '? 9 1 * * ?', '0 9 1 * ? ?',
        '0 9 1 ?/2 * MON', '0 9 1 *,? * MON', '0 9 1 ? * 0', '0 9 1 ? * 8',
        '0 9 1 ? * 7-2', '0 9 1 ? * 1/0', '0 9 1 ? * 1/8', '0 9 1 ? * FOO',
        '60 9 1 * * ?', '0 60 1 * * ?', '0 9 24 * * ?', '0 9 1 31 FEB ?',
        '0 9 1 L * ?', '0 9 1 ? * MON#2', '0 9 1 1W * ?', '0 9 1 * * ? 2026',
        '9 1 ? * MON'
    ]) assert.throws(() => preview(expression), undefined, expression);
});
