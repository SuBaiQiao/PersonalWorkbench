import test from 'node:test';
import assert from 'node:assert/strict';
import { parseJson, queryJson, compareText, decodeJwt, jwtDates, generateRandom, secureIndex } from '../src/utils/developerTools.js';
import { generateModels } from '../src/utils/modelGenerator.js';
import { evaluateRegex } from '../src/utils/regex.js';
import { exportCurl, importCurl, readResponse, validateRequest } from '../src/utils/httpTool.js';
import { validateImageTransform } from '../src/utils/imageTool.js';

test('JSON 错误定位与安全 JSONPath 查询', () => {
    assert.throws(() => parseJson('{\n"name":,\n}'), /第 2 行，第 8 列/);
    const input = parseJson('{"users":[{"name":"甲"},{"name":"乙"}]}');
    assert.deepEqual(queryJson(input,'$.users[*].name'),['甲','乙']);
    assert.deepEqual(queryJson(input,'$..name'),['甲','乙']);
    assert.deepEqual(queryJson(input,'$.absent'),[]);
    assert.throws(() => queryJson(input,'$.users[?(@.name)]'));
    assert.throws(() => queryJson(input,'users'));
});
test('文本对比保留原文行号，并按选项忽略大小写与空白', () => {
    const result = compareText('A\r\nB\nC','A\nB2\nC\nD');
    assert.equal(result.added,2); assert.equal(result.removed,1);
    assert.equal(result.rows.find(row=>row.type==='removed').leftLine,2);
    const same = compareText(' a B\t','AB',{ ignoreWhitespace:true, ignoreCase:true });
    assert.equal(same.added + same.removed,0); assert.equal(same.rows[0].left,' a B\t');
    assert.equal(compareText('a\n','a').removed,1);
    assert.throws(()=>compareText('x'.repeat(200001),''),/20 万/);
});
test('JWT 正确处理 Unicode 和 NumericDate，拒绝非对象与非法编码', () => {
    const enc = value => Buffer.from(JSON.stringify(value)).toString('base64url');
    const result = decodeJwt(`Bearer ${enc({alg:'none'})}.${enc({name:'测试🙂',exp:1})}.`);
    assert.equal(result.payload.name,'测试🙂'); assert.equal(jwtDates(result.payload,2000)[0].state,'已过期');
    assert.equal(jwtDates({exp:'1'},2000)[0].date,'无效 NumericDate');
    assert.throws(()=>decodeJwt(`${enc([])}.${enc({})}.abc`));
    assert.throws(()=>decodeJwt('a.aa.aaa')); assert.throws(()=>decodeJwt('a.b'));
});
test('随机生成符合 UUID 规范、字符集及密码字符覆盖', () => {
    const uuids = generateRandom({mode:'uuid',count:100});
    assert.equal(new Set(uuids).size,100);
    uuids.forEach(value=>assert.match(value,/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/));
    generateRandom({mode:'password',count:30,length:8,groups:['lower','upper','digits','symbols']}).forEach(value => {
        assert.equal(value.length,8); assert.match(value,/[a-z]/); assert.match(value,/[A-Z]/); assert.match(value,/\d/); assert.match(value,/[^a-z0-9]/i);
    });
    assert.deepEqual(generateRandom({mode:'number',count:3,min:-1,max:-1}),['-1','-1','-1']);
    assert.throws(()=>generateRandom({mode:'password',groups:[]}));
    assert.throws(()=>generateRandom({count:501}));
    let calls=0;
    assert.equal(secureIndex(3,{getRandomValues(buffer){buffer[0]=calls++ ? 5 : 0xffffffff;}}),2);
    assert.equal(calls,2);
});
test('正则分组、零宽 Unicode 匹配与替换语义一致', () => {
    for (const [pattern,flags,text,replacement] of [
        ['(?<year>\\d{4})-(\\d{2})','g','2026-10 / 2027-01','$<year>/$2 $$ $&'],
        ['(?:)','gu','🙂a','X'], ['(a)?b','g','b ab',"$1 $12 $09 $` $'"],
        ['[a-z]','','abc','$&!'], ['z','g','abc','!']
    ]) assert.equal(evaluateRegex({pattern,flags,text,replacement}).replaced,text.replace(new RegExp(pattern,flags),replacement));
    assert.equal(evaluateRegex({pattern:'',flags:'gu',text:'🙂',replacement:''}).matches.length,2);
    assert.throws(()=>evaluateRegex({pattern:'[',flags:'g',text:'',replacement:''}));
    assert.throws(()=>evaluateRegex({pattern:'a',flags:'g',text:'a'.repeat(1000),replacement:'x'.repeat(1000)}),/30 万/);
});
test('模型生成合并数组缺失字段、空值与嵌套类型', () => {
    const input = '[{"user_id":1,"note":"x","child":{"a":true}},{"user_id":2,"child":null}]';
    const cs = generateModels(input);
    assert.match(cs,/JsonPropertyName\("user_id"\)/); assert.match(cs,/public long UserId/); assert.match(cs,/public string\? Note/);
    assert.match(cs,/Child\?/); assert.match(cs,/根类型：List</);
    const ts = generateModels(input,{language:'typescript',rootName:'Result'});
    assert.match(ts,/export type Result = Array</); assert.match(ts,/"note"\?: string/); assert.match(ts,/null/);
    assert.throws(()=>generateModels('{}',{rootName:'bad name'}));
    const collision = generateModels('{"root":1,"a-b":2,"a_b":3,"class":4}');
    assert.match(collision,/public long Root2/); assert.match(collision,/public long AB2/);
});
test('混合类型和特殊字段不会生成无效的类型或字段名称', () => {
    const cs = generateModels('{"a":[1,"text"],"b":[1,1.5],"1!":null,"weird\\\"key":true}');
    assert.match(cs,/List<JsonElement>/); assert.match(cs,/List<double>/); assert.match(cs,/public JsonElement\? _1/);
    assert.match(cs,/JsonPropertyName\("weird\\"key"\)/);
});
test('cURL 单引号与换行导入导出往返，不执行 shell 内容', () => {
    const request = {method:'POST',url:'https://example.com/api?a=1',headers:'Content-Type: application/json\nX-Test: a:b',body:JSON.stringify({name:"O'Reilly",text:'$(whoami)\nline'})};
    assert.deepEqual(importCurl(exportCurl(request)),request);
    assert.equal(importCurl("curl --json '{\"a\":1}' https://example.com").method,'POST');
    assert.match(importCurl("curl https://example.com -d ''").headers,/application\/x-www-form-urlencoded/);
    assert.equal(importCurl("curl https://example.com -d ''").method,'POST');
    assert.throws(()=>importCurl("curl https://example.com --proxy localhost"),/暂不支持/);
    assert.throws(()=>importCurl("curl https://example.com -d @secret"),/@文件/);
    assert.throws(()=>importCurl("curl 'https://example.com"),/引号/);
    assert.throws(()=>validateRequest({url:'file:///tmp/a'}));
    assert.throws(()=>validateRequest({url:'https://example.com',method:'GET',body:'{}'}));
    assert.throws(()=>validateRequest({url:'https://example.com',headers:'bad header'}));
});
test('HTTP 响应读取支持 UTF-8 跨块并限制大响应', async () => {
    const bytes = new TextEncoder().encode('你好🙂');
    const response = new Response(new ReadableStream({start(controller){ controller.enqueue(bytes.slice(0,2)); controller.enqueue(bytes.slice(2)); controller.close(); }}));
    assert.deepEqual(await readResponse(response),{text:'你好🙂',bytes:bytes.length});
    await assert.rejects(readResponse(new Response('too large'),2),/2 MB/);
});
test('图片裁剪与输出尺寸限制', () => {
    const params = {sourceWidth:1000,sourceHeight:800,x:10,y:20,cropWidth:500,cropHeight:300,width:250,height:150};
    assert.doesNotThrow(()=>validateImageTransform(params));
    assert.throws(()=>validateImageTransform({...params,x:900}),/裁剪范围/);
    assert.throws(()=>validateImageTransform({...params,width:0}),/输出尺寸/);
    assert.throws(()=>validateImageTransform({...params,width:8192,height:8192}),/4000 万/);
});
