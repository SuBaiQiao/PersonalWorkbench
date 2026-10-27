# 全国行政区划快照

- 上游：[mumuy/data_location](https://github.com/mumuy/data_location)，MIT 许可见 `china-areas.LICENSE`。
- 原始文件：[list.json](https://github.com/mumuy/data_location/blob/1756d559ede7161b5d5a8d062a466beeb31cb714/list.json)。
- 固定提交：`1756d559ede7161b5d5a8d062a466beeb31cb714`；下载日期：2026-10-27。
- `china-areas.json` 保留原始六位编码与名称，嵌入 API 程序集，部署和使用时无需联网。

服务从平铺的代码表构建省、市、县三级树。直辖市补充“市辖区 / 县”分组，省或自治区直辖县级单位补充对应分组。东莞、中山、儋州、嘉峪关等没有县级子节点的城市，以城市自身六位代码作为可选叶节点，不引入乡镇或街道统计代码。

省级列表覆盖 34 个省级地区。上游港澳台编码属于自定义地址编码，其中台湾省使用 `830000`，并非标准行政区划代码。港澳台保留上游数据用于区划浏览，`supportsIdentityGeneration` 返回 `false`；随机生成和定向生成仅使用大陆 31 个省级地区的数据。

更新时替换上游 `list.json` 快照，并同步本文件的固定提交、下载日期和许可信息。不要将乡镇九位或统计用十二位编码混入快照。运行以下检查确认所有源条目均进入三级树、全部生成叶节点的号码规则有效、非法层级组合被拒绝：

```powershell
dotnet run --project tests/IdentityGeneratorChecks/IdentityGeneratorChecks.csproj
```
