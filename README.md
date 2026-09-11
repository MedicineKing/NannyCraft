# NannyCraft

一键装齐的懒人 Minecraft 启动器 · 坏了自己修

## 这是什么

一个给自己和朋友用的 Minecraft 启动器(Windows · .NET 8 · WPF)。
目标一句话:**你只管点开始,剩下的交给 NannyCraft。**

- **一键装齐**:客户端、库文件、原生库、资源对象树(几千个文件)全自动并行下载,SHA-1 全程校验,已存在的自动跳过
- **坏了自己修**:启动前自检,缺什么提示什么、一键补全;下载中断了再点一次就是续上
- **离线可玩**:自动探测本机 Java(JAVA_HOME / 注册表 / 常见目录 / PATH 四路),需要哪个版本按官方 `javaVersion` 映射来
- **下载快**:并发数自动(按 CPU 核数)或手动调节
- **深色界面**:主题色可换、主页背景图可设;动效遵循 Apple HIG / HarmonyOS 规范(150/220/350ms 三档,跟随系统"减少动画")
- **今日 MC 快讯**:官方补丁说明源,可走代理、可自动翻译
- **反馈直达**:关于页一键跳 GitHub Issues,自动附带启动器标识 / 系统 / 硬件信息 / 运行日志(含崩溃日志)

## 截图

待补

## 构建

```bash
dotnet build

# 发布为单文件(自包含)
dotnet publish -c Release -r win-x64 --self-contained
```

需要 .NET 8 SDK;窗口基于 [WPF-UI](https://github.com/lepoco/wpfui)(MIT)。

## 路线图

- [x] 完整安装链(客户端 / 库 / 原生库 / 资源对象树 / 日志配置)
- [x] 离线启动 + 官方参数模板
- [ ] 内置 JDK 下载(Java 21 / 25)
- [ ] 更新系统(多通道 + 差分更新)
- [ ] 联机(KCP 打洞优先 + 中继兜底)
- [ ] 服主 / 服务端工具箱

## 反馈

- 启动器内:**关于 → 帮助与反馈**(环境信息与日志自动附上,推荐)
- 或在 [Issues](https://github.com/MedicineKing/NannyCraft/issues) 里按模板提交

## 说明

- 非官方产品,与 Mojang / Microsoft 无关联;不提供盗版或破解
- 零遥测、零数据收集;所有配置与文件都在你自己手里
- 许可:[MIT](LICENSE)
