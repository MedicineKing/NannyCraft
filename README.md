<div align="center">

# NannyCraft

**一键装齐的懒人 Minecraft 启动器 · 坏了自己修**

[![Stars](https://img.shields.io/github/stars/MedicineKing/NannyCraft?style=flat-square&labelColor=444444&color=eac54f)](https://github.com/MedicineKing/NannyCraft/stargazers)
[![Issues](https://img.shields.io/github/issues/MedicineKing/NannyCraft?style=flat-square&labelColor=444444&color=1F883D)](https://github.com/MedicineKing/NannyCraft/issues)
[![Discussions](https://img.shields.io/badge/Discussions-open-6E7781?style=flat-square&logo=github)](https://github.com/MedicineKing/NannyCraft/discussions)
[![License](https://img.shields.io/github/license/MedicineKing/NannyCraft?style=flat-square&labelColor=444444)](https://github.com/MedicineKing/NannyCraft/blob/main/LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square&logo=windows11&logoColor=white)](#)

</div>

---

## 这是什么

一个给自己和朋友用的 Minecraft 启动器(Windows · .NET 8 · WPF)。

目标一句话:**你只管点开始,剩下的交给 NannyCraft。**

## 功能

- **一键装齐**:客户端、库文件、原生库、资源对象树(几千个文件)全自动并行下载,SHA-1 全程校验,已存在的自动跳过
- **坏了自己修**:启动前自检,缺什么提示什么、一键补全;下载中断了再点一次就是续上
- **离线可玩**:自动探测本机 Java(JAVA_HOME / 注册表 / 常见目录 / PATH 四路),需要哪个版本按官方 `javaVersion` 映射来
- **下载快**:并发数自动(按 CPU 核数)或手动调节
- **深色界面**:主题色可换、主页背景图可设;动效遵循 Apple HIG / HarmonyOS 规范(150/220/350ms 三档,跟随系统"减少动画")
- **今日 MC 快讯**:官方补丁说明源,可走代理、可自动翻译
- **反馈直达**:关于页一键跳 GitHub Issues,自动附带启动器标识 / 系统 / 硬件信息 / 运行日志(含崩溃日志)

## 构建

> 首个正式版还在打磨中,暂未提供打包下载;发布后这里会补上 Releases 链接。

```bash
dotnet build

# 发布为单文件(自包含)
dotnet publish -c Release -r win-x64 --self-contained
```

需要 .NET 8 SDK。

## 路线图

- [x] 完整安装链(客户端 / 库 / 原生库 / 资源对象树 / 日志配置)
- [x] 离线启动 + 官方参数模板
- [ ] 内置 JDK 下载(Java 21 / 25)
- [ ] 更新系统(多通道 + 差分更新)
- [ ] 联机(KCP 打洞优先 + 中继兜底)
- [ ] 服主 / 服务端工具箱

## 反馈与贡献

- **启动器内**:关于 → 帮助与反馈(环境信息与日志自动附上,推荐)
- **Issues**:[按模板提交](https://github.com/MedicineKing/NannyCraft/issues/new/choose) Bug 或功能建议
- **Discussions**:[想法 / 问答 / 展示](https://github.com/MedicineKing/NannyCraft/discussions)都可以聊
- **代码**:欢迎 Fork + PR(PR 模板已备好)

## 致谢

- [WPF-UI](https://github.com/lepoco/wpfui)(MIT)· WPF / .NET
- Mojang 公开接口(版本清单 / 资源 / 补丁说明)

## 说明

- 非官方产品,与 Mojang / Microsoft 无关联;不提供盗版或破解
- 零遥测、零数据收集;所有配置与文件都在你自己手里

## 许可

[MIT](LICENSE)
