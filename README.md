# BTC交易管理系统

当前版本：`v0.6.7 启动崩溃修复版`

## v0.6.7 修复内容

### 修复双击程序没反应

v0.6.6 在启动时会恢复表格列宽，如果本地 `ui_settings.json` 中保存了某些异常列宽，WinForms 可能在 `DataGridViewBand.set_Thickness` 处触发空引用崩溃，表现为：

```text
双击 EXE 没反应
程序瞬间退出
```

已修复为：

```text
列宽恢复失败时自动忽略
列宽限制在安全范围内
恢复失败时回退到自动适应列宽
```

同时我已把本地旧的 `ui_settings.json` 备份为：

```text
ui_settings.backup_before_v067.json
```

并清空旧布局，让新版重新生成安全布局。

## 验证情况

已验证：

```text
dotnet build 成功
EXE 发布成功
EXE 启动成功
启动 6 秒后进程仍在运行
ui_settings.json 自动重新生成
GitHub 推送成功
Release 发布成功
```

## GitHub

```text
https://github.com/mingzhoukeji777/BTC-Trade-Manager
```
