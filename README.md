# BTC交易管理系统

当前版本：`v0.7.7 OKX U本位联合保证金账户版`

## v0.7.7 新增/调整

### 1. 新增 OKX U本位永续主仓账户

当前主仓下拉新增：

```text
OKX U本位主仓(BTC联合保证金)
```

账户键：

```text
OKX_UM
```

数据库会自动初始化：

```text
OKX_UM / M1
```

备注：

```text
OKX U本位永续｜联合保证金模式｜保证金币种BTC
```

### 2. 设置里增加启用账户选项

设置窗口新增：

```text
启用 Binance U本位
启用 OKX 币本位
启用 OKX U本位(BTC联合保证金)
```

只有启用的账户才会显示在顶部 `当前主仓` 下拉列表里。

### 3. OKX U本位快照识别

管理系统现在会从采集器 `latest_snapshot.json` 中识别：

```text
OKX U本位
```

并绑定到：

```text
OKX U本位主仓(BTC联合保证金)
```

### 4. OKX U本位的呈现方式

由于你 OKX U本位永续使用联合保证金，保证金币种是 BTC，所以软件中这样处理：

```text
账户/保证金：按 BTC 联合保证金提示展示
交易合约：按 U本位 BTC-USDT-SWAP 管理
手续费/成交价格：按 U本位 USDT 价格体系处理
主仓说明：OKX U本位永续｜联合保证金｜保证金币种BTC
```

也就是说，UI 上会明确显示这是：

```text
OKX U本位 + BTC联合保证金
```

避免和 OKX 币本位主仓混淆。

## 本地 EXE

```text
C:\Users\xiaozhu\Desktop\BTC交易管理系统\BTC交易管理系统_v0.7.7.exe
```

## 验证情况

```text
dotnet build 成功
EXE 发布成功
未自动启动程序
OKX_UM 账户键存在
启用账户配置存在
当前主仓下拉包含 OKX U本位主仓(BTC联合保证金)
latest_snapshot 识别 OKX U本位
设置窗口启用项存在
数据库种子 OKX_UM/M1 存在
```
