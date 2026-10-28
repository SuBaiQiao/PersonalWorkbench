import test from 'node:test';
import assert from 'node:assert/strict';
import { cellText, fieldEditor, fieldValue, columnEditor, columnDefinition, columnPayload } from '../src/utils/databaseManagement.js';

test('编辑器区分 NULL、空文本与省略字段', () => {
    for (const value of [null, '', '中文\ntext', 0, -12, 1.25]) assert.deepEqual(fieldValue(fieldEditor(value)), value);
    assert.equal(fieldValue(fieldEditor(undefined, true)), undefined);
    assert.equal(cellText(null), 'NULL');
    assert.equal(cellText(''), '');
});
test('BLOB 和 64 位整数往返无损', () => {
    for (const value of [{ $type: 'blob', base64: 'AP8=' }, { $type: 'integer', value: '9223372036854775807' }, { $type: 'integer', value: '-9223372036854775808' }])
        assert.deepEqual(fieldValue(fieldEditor(value)), value);
    assert.equal(cellText({ $type: 'blob', base64: 'AP8=' }), 'BLOB · 2 bytes');
    assert.equal(cellText({ $type: 'integer', value: '9223372036854775807' }), '9223372036854775807');
});
test('拒绝无效数字、越界整数和无效 Base64，防止静默转换', () => {
    for (const field of [{ mode: 'number', value: '' }, { mode: 'number', value: 'NaN' }, { mode: 'number', value: 'Infinity' }, { mode: 'number', value: '9223372036854775807' }, { mode: 'integer', value: '9223372036854775808' }, { mode: 'integer', value: '1.2' }, { mode: 'blob', value: '%invalid' }])
        assert.throws(() => fieldValue(field));
});

test('选择式字段编辑正确读取主键、自增、非空及命名约束', () => {
    const editor = columnEditor({ name: 'Id', type: 'INTEGER', definition: 'INTEGER NOT NULL CONSTRAINT "PK_sample" PRIMARY KEY AUTOINCREMENT' });
    assert.equal(editor.advanced, false);
    assert.equal(editor.primaryKey, true); assert.equal(editor.autoIncrement, true); assert.equal(editor.notNull, true);
    assert.match(columnDefinition(editor), /CONSTRAINT "PK_sample" PRIMARY KEY AUTOINCREMENT/);
    editor.autoIncrement = false;
    assert.doesNotMatch(columnDefinition(editor), /AUTOINCREMENT/);
});
test('默认值中的引号和 SQL 关键词不被当作约束', () => {
    const editor = columnEditor({ name: 'value', type: 'TEXT', definition: "TEXT DEFAULT 'a''b NOT NULL' NOT NULL UNIQUE" });
    assert.equal(editor.advanced, false); assert.equal(editor.defaultValue, "a'b NOT NULL");
    assert.equal(editor.notNull, true); assert.equal(editor.unique, true);
    assert.equal(columnDefinition(editor), "TEXT NOT NULL UNIQUE DEFAULT 'a''b NOT NULL'");
});
test('复杂约束保留完整原文，不会被选择表单静默丢弃', () => {
    for (const definition of ['TEXT CHECK(length(value)>0)', 'TEXT COLLATE NOCASE', 'INTEGER REFERENCES parent(id)', 'INTEGER GENERATED ALWAYS AS (a*2) STORED', 'INTEGER PRIMARY KEY DESC', 'TEXT DEFAULT (lower(hex(randomblob(4))))']) {
        const editor = columnEditor({ name: 'value', definition });
        assert.equal(editor.advanced, true); assert.equal(columnDefinition(editor), definition);
    }
    const composite = columnEditor({ name: 'a', type: 'TEXT', primaryKey: 1, definition: 'TEXT NOT NULL' });
    assert.equal(composite.externalPrimaryKey, true);
    assert.doesNotMatch(columnDefinition(composite), /PRIMARY KEY/);
});
test('索引选择仅修改独立单字段索引，保持未改变的索引', () => {
    const editor = columnEditor({ name: 'name', type: 'TEXT', definition: 'TEXT' }, [
        { origin: 'c', partial: false, unique: false, columns: ['name'] },
        { origin: 'c', partial: false, unique: true, columns: ['name', 'id'] },
        { origin: 'u', partial: false, unique: true, columns: ['name'] }
    ]);
    assert.equal(editor.index, 'index'); assert.equal(columnPayload(editor).index, null);
    editor.index = 'unique'; assert.equal(columnPayload(editor).index, 'unique');
    const added = columnEditor(); added.name = 'new'; assert.equal(columnPayload(added).index, 'none');
});
test('选择表单校验自增类型和数值默认值', () => {
    const editor = columnEditor(); editor.autoIncrement = true;
    assert.throws(() => columnDefinition(editor), /INTEGER 主键/);
    editor.autoIncrement = false; editor.defaultMode = 'number'; editor.defaultValue = 'abc';
    assert.throws(() => columnDefinition(editor), /有效数值/);
    editor.defaultValue = '-12.5'; assert.equal(columnDefinition(editor), 'TEXT DEFAULT -12.5');
});
