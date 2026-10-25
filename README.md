# 拾印 PhotoMark 1.4

中文离线 Windows 照片水印工具。暗黑界面、自然风景预览、批量导出，原图始终保留。

## 功能

- 文字水印或透明 PNG Logo，支持 JPG / PNG 照片。
- 八种模式：五点分布、均匀平铺、斜向平铺、中心散射、圆形环绕、十字分布、螺旋、多层圆环。
- 滑块调整字号、透明度，以及适用模式的间距或半径。
- 数量自动安排，旋转角度和安全边距由模式确定，不再显示对应控件。
- 默认文字“拾印 PhotoMark”，内置风景仅用于预览，不参与导出。
- 重名自动编号、失败项汇总、取消任务、设置记忆。
- 全程本地处理，无运行时联网。

## 使用

解压 Windows x64 便携包，运行 PhotoMark.exe。添加照片或拖入文件夹，调整样式后导出。

## 构建

需要 Windows 和 .NET 10 SDK，无额外 NuGet 依赖。

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

## 验证

```powershell
Start-Process ./dist/PhotoMark.exe -ArgumentList '--self-test', "$PWD/test-results" -Wait
Start-Process ./dist/PhotoMark.exe -ArgumentList '--ui-check', "$PWD/test-results" -Wait
```

先运行图像测试，再运行界面测试，每次使用新的测试目录。测试结果记录在目录内。界面测试是进程内检查，不等同真实鼠标与系统文件对话框测试。当前在 Windows 10 上验证，未独立验证 Windows 11 与多显示器 DPI 切换。

## 结构

MainForm.cs 管理界面与批处理，StudioControls.cs 提供主题和滑块，WatermarkLayouts.cs 生成位置布局，Imaging.cs 渲染与导出，Settings.cs 保存设置，SelfTest.cs 提供图像检查。

## 许可证

原创程序代码采用 MIT。Assets/nature.jpg 为独立授权的第三方照片，不适用 MIT，详见 THIRD_PARTY_NOTICES.md。自包含程序还附带 .NET 运行时的原始许可证。
