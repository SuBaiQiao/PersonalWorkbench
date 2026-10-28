-- 由 MenuDatabaseInitializer 在菜单表为空的事务中执行。
-- $categories 和 $tools 是程序集内置的初始 JSON 数据，仅用于首次初始化。
INSERT INTO menu_categories (Id, Name, "Order")
SELECT json_extract(value, '$.id'), json_extract(value, '$.name'), json_extract(value, '$.order')
FROM json_each($categories) WHERE 1
ON CONFLICT(Id) DO NOTHING;

INSERT INTO tool_menus (Name, Route, ComponentKey, Category, Icon, Tone, Description, Keywords, Features, "Order", IsEnabled)
SELECT json_extract(value, '$.name'), json_extract(value, '$.route'), json_extract(value, '$.componentKey'),
       json_extract(value, '$.category'), json_extract(value, '$.icon'), json_extract(value, '$.tone'),
       json_extract(value, '$.description'), json_extract(value, '$.keywords'), json_extract(value, '$.features'),
       json_extract(value, '$.order'), json_extract(value, '$.isEnabled')
FROM json_each($tools);
