# AI系列生产工具

Windows 桌面端 AI 图像生产工作台：提示词、多参考图、生成队列、画幅与质量档位、历史归档、新建项目、批量重命名和在线检查更新。

当前公开构建版本：`3.2.0.65`

## 构建

仓库只使用 Windows 自带的 .NET Framework C# 编译器，不依赖 Visual Studio。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物：

- `out/AI-Series-Production-Tool.exe`：主程序
- `out/AI-Series-Production-Tool_3.2.0.65-Setup.exe`：安装向导

GitHub Actions 会自动执行同一脚本，并上传安装向导作为构建产物。

## 目录

- `src/AppCore.cs`、`AppPages.cs`、`AppDialogs.cs`、`AppMain.cs`：主程序
- `src/SetupFinal.cs`：安装向导
- `src/VersionInfo.cs`：版本信息
- `src/main.ico`、`src/setup.ico`：程序与安装向导图标

## 说明

- 软件不内置账号系统，API 地址与 Key 由用户在本地配置，仓库不含任何密钥。
- 安装包与主程序均未内置第三方捆绑软件。
- 本项目公开源码仅用于可复现构建与代码签名审核。
