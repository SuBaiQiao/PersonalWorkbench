import test from 'node:test';
import assert from 'node:assert/strict';
import { describeInstant, parseDateTime, parseTimestamp } from '../src/utils/time.js';
import { inspectCron } from '../src/utils/cron.js';

test('seconds and milliseconds denote the same instant', () => {
    assert.equal(parseTimestamp('1700000000', 'seconds'), parseTimestamp('1700000000000'));
    assert.equal(describeInstant(0, 'Asia/Shanghai').date, '1970-01-01 08:00:00.000 +08:00');
    assert.equal(parseTimestamp('-1'), -1);
    assert.equal(describeInstant(-1, 'UTC').seconds, '-1');
});
test('invalid and out-of-range timestamps are rejected', () => {
    for (const value of ['', '1.5', '1e3', 'NaN', '9007199254740992', '8640000000000001']) assert.throws(() => parseTimestamp(value));
});
test('dates respect zone and explicit offset', () => {
    assert.equal(parseDateTime('1970-01-01 08:00:00', 'Asia/Shanghai'), 0);
    assert.equal(parseDateTime('1970-01-01T00:00:00Z', 'Asia/Shanghai'), 0);
    assert.equal(parseDateTime('1970-01-01T08:00:00+08:00', 'UTC'), 0);
    assert.equal(parseDateTime('2024-02-29 01:02:03.456', 'UTC'), Date.UTC(2024,1,29,1,2,3,456));
});
test('calendar rollover, invalid time and invalid zone are rejected', () => {
    for (const text of ['2025-02-29 00:00:00', '2026-04-31 00:00:00', '2026-01-01 25:00:00', '2026-01-01']) assert.throws(() => parseDateTime(text, 'UTC'));
    assert.throws(() => parseDateTime('2026-01-01 00:00:00', 'bad/zone'));
});
test('DST gap and ambiguity require an explicit valid instant', () => {
    assert.throws(() => parseDateTime('2026-03-08 02:30:00', 'America/New_York'), /不存在/);
    assert.throws(() => parseDateTime('2026-11-01 01:30:00', 'America/New_York'), /两次/);
    assert.equal(parseDateTime('2026-11-01T01:30:00-05:00'), Date.UTC(2026,10,1,6,30));
});
test('cron preview starts strictly after the selected instant', () => {
    const result = inspectCron('*/5 * * * *', { start: Date.UTC(2026,9,22,0,0), count: 2 });
    assert.equal(result.occurrences[0].utc, '2026-10-22T00:05:00.000Z');
    assert.equal(result.occurrences[1].utc, '2026-10-22T00:10:00.000Z');
    assert.match(result.description, /5/);
    assert.match(result.description, /分钟/);
});
test('cron weekdays, named months and selected zone are honored', () => {
    const result = inspectCron('0 9 * * MON-FRI', { zone: 'Asia/Shanghai', start: Date.UTC(2026,9,23,2), count: 1 });
    assert.equal(result.occurrences[0].utc, '2026-10-26T01:00:00.000Z');
    assert.equal(inspectCron('0 0 1 JUL *', { start: Date.UTC(2026,0,1), count: 1 }).occurrences[0].utc, '2026-07-01T00:00:00.000Z');
});
test('cron uses OR for day-of-month and day-of-week', () => {
    const result = inspectCron('0 0 1 * MON', { start: Date.UTC(2026,9,22), count: 1 });
    assert.equal(result.dayOr, true);
    assert.match(result.description, /或/);
    assert.equal(result.occurrences[0].utc, '2026-10-26T00:00:00.000Z');
});
test('cron finds leap days across years', () => {
    assert.equal(inspectCron('0 0 29 2 *', { start: Date.UTC(2025,0,1), count: 1 }).occurrences[0].utc, '2028-02-29T00:00:00.000Z');
});
test('cron rejects unsupported syntax, impossible dates and counts', () => {
    for (const value of ['0 0 0 * * *', '61 * * * *', '0 0 31 2 *', '0 0 ? * MON', '0 0 L * *', '0 0 * * 1#2']) assert.throws(() => inspectCron(value));
    assert.throws(() => inspectCron('* * * * *', {count:0}));
    assert.throws(() => inspectCron('* * * * *', {count:21}));
});
