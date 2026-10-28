CREATE TABLE IF NOT EXISTS menu_categories (
    Id TEXT NOT NULL CONSTRAINT PK_menu_categories PRIMARY KEY,
    Name TEXT NOT NULL,
    "Order" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS tool_menus (
    Id INTEGER NOT NULL CONSTRAINT PK_tool_menus PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Route TEXT NOT NULL COLLATE NOCASE,
    ComponentKey TEXT NOT NULL,
    Category TEXT NOT NULL,
    Icon TEXT NULL,
    Tone TEXT NOT NULL,
    Description TEXT NOT NULL,
    Keywords TEXT NOT NULL,
    Features TEXT NOT NULL,
    "Order" INTEGER NOT NULL,
    IsEnabled INTEGER NOT NULL,
    CONSTRAINT FK_tool_menus_menu_categories_Category FOREIGN KEY (Category)
        REFERENCES menu_categories (Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_tool_menus_Route ON tool_menus (Route);
CREATE INDEX IF NOT EXISTS IX_tool_menus_Category ON tool_menus (Category);
