using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BtcTradeManager;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Global\\BTCTradeManager_XiaoZhu_v03", out var created);
        if (!created) return;
        Application.Run(new MainForm());
    }
}

public sealed class AppConfig
{
    public string CollectorLatestPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BTC实时通信系统", "data", "latest_snapshot.json");
    public string DataDir { get; set; } = "";
    public int RefreshSeconds { get; set; } = 3;
    public decimal DefaultFeeRatePct { get; set; } = 0.05m;
    public decimal MaxSingleRiskPct { get; set; } = 0.35m;
}
public sealed class UiSettings
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Width { get; set; } = 1360;
    public int Height { get; set; } = 820;
    public int SelectedTab { get; set; } = 0;
}
public sealed class SnapshotRoot { public string? schema_version { get; set; } public string? updated_at { get; set; } public List<CollectorSnapshot> snapshots { get; set; } = new(); }
public sealed class CollectorSnapshot
{
    public string Exchange { get; set; } = ""; public string Symbol { get; set; } = ""; public string Price { get; set; } = "--"; public string Funding { get; set; } = "--"; public string Equity { get; set; } = "--"; public string Available { get; set; } = "--"; public string Position { get; set; } = "--"; public string Entry { get; set; } = "--"; public string Mark { get; set; } = "--"; public string Upnl { get; set; } = "--"; public string Liq { get; set; } = "--"; public string Status { get; set; } = "--"; public string LastSuccess { get; set; } = ""; public int ConsecutiveFailures { get; set; }
}

public sealed class Store
{
    public readonly string BaseDir; public readonly string ConfigPath; public readonly string UiPath;
    public AppConfig Config { get; private set; } = new();
    public string DataDir => string.IsNullOrWhiteSpace(Config.DataDir) ? Path.Combine(BaseDir, "data") : Config.DataDir;
    public string DbPath => Path.Combine(DataDir, "btc_trade_manager.sqlite3");
    public Store()
    {
        BaseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        ConfigPath = Path.Combine(BaseDir, "manager_config.json"); UiPath = Path.Combine(BaseDir, "ui_settings.json");
        LoadConfig(); if (!File.Exists(ConfigPath)) SaveConfig(Config); InitDb();
    }
    public void LoadConfig(){ try{ if(File.Exists(ConfigPath)) Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath,Encoding.UTF8), new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new AppConfig(); }catch{ Config=new AppConfig(); } if(string.IsNullOrWhiteSpace(Config.DataDir)) Config.DataDir=Path.Combine(BaseDir,"data"); }
    public void SaveConfig(AppConfig cfg){ Config=cfg; Directory.CreateDirectory(BaseDir); File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg,new JsonSerializerOptions{WriteIndented=true}), Encoding.UTF8); InitDb(); }
    public UiSettings LoadUi(){ try{ if(File.Exists(UiPath)) return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(UiPath,Encoding.UTF8)) ?? new UiSettings(); }catch{} return new UiSettings(); }
    public void SaveUi(UiSettings ui){ try{ File.WriteAllText(UiPath, JsonSerializer.Serialize(ui,new JsonSerializerOptions{WriteIndented=true}), Encoding.UTF8); }catch{} }
    public void InitDb()
    {
        Directory.CreateDirectory(DataDir);
        using var cn = new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"
CREATE TABLE IF NOT EXISTS main_positions(
 id INTEGER PRIMARY KEY AUTOINCREMENT, account_key TEXT NOT NULL, code TEXT NOT NULL, direction TEXT NOT NULL,
 cycle_name TEXT, status TEXT, base_qty REAL DEFAULT 0, avg_price REAL DEFAULT 0, margin REAL DEFAULT 0,
 created_at TEXT NOT NULL, note TEXT, UNIQUE(account_key,code)
);
CREATE TABLE IF NOT EXISTS position_nodes(
 id INTEGER PRIMARY KEY AUTOINCREMENT, account_key TEXT NOT NULL, main_code TEXT NOT NULL, parent_id INTEGER,
 code TEXT NOT NULL, node_type TEXT NOT NULL, direction TEXT, status TEXT, qty REAL DEFAULT 0,
 entry_price REAL DEFAULT 0, close_price REAL, fee REAL DEFAULT 0, funding REAL DEFAULT 0,
 breakeven_price REAL DEFAULT 0, cost_stop REAL, trailing_take_profit REAL,
 exchange_open_order_id TEXT, exchange_close_order_id TEXT, created_at TEXT NOT NULL, note TEXT,
 UNIQUE(account_key, main_code, code)
);
CREATE TABLE IF NOT EXISTS order_mappings(
 id INTEGER PRIMARY KEY AUTOINCREMENT, account_key TEXT NOT NULL, node_id INTEGER, order_id TEXT, trade_id TEXT,
 side TEXT, action TEXT, price REAL, qty REAL, fee REAL, funding REAL DEFAULT 0, ts TEXT, match_status TEXT, raw_note TEXT
);
CREATE TABLE IF NOT EXISTS funding_records(id INTEGER PRIMARY KEY AUTOINCREMENT, account_key TEXT NOT NULL, main_code TEXT, node_id INTEGER, amount REAL, ts TEXT, source TEXT, note TEXT);
CREATE TABLE IF NOT EXISTS scenario_runs(id INTEGER PRIMARY KEY AUTOINCREMENT, account_key TEXT NOT NULL, target_price REAL, created_at TEXT NOT NULL, result_json TEXT);
INSERT OR IGNORE INTO main_positions(account_key,code,direction,cycle_name,status,created_at,note) VALUES
 ('BINANCE_UM','M1','多','当前牛熊周期','规划中',datetime('now','localtime'),'Binance U本位独立长线主仓'),
 ('OKX_COIN','M1','多','当前牛熊周期','规划中',datetime('now','localtime'),'OKX 币本位独立长线主仓');
";
        cmd.ExecuteNonQuery();
    }
    public DataTable Query(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); using var r=cmd.ExecuteReader(); var dt=new DataTable(); dt.Load(r); return dt; }
    public void Exec(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); cmd.ExecuteNonQuery(); }
    public object? Scalar(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); return cmd.ExecuteScalar(); }
    public SnapshotRoot? LoadLatest(out string err){ err=""; try{ if(!File.Exists(Config.CollectorLatestPath)){ err="未找到采集器 latest_snapshot.json"; return null; } return JsonSerializer.Deserialize<SnapshotRoot>(File.ReadAllText(Config.CollectorLatestPath,Encoding.UTF8), new JsonSerializerOptions{PropertyNameCaseInsensitive=true}); }catch(Exception ex){ err=ex.Message; return null; } }
}

public sealed class MainForm:Form
{
    readonly Store _store = new(); readonly ComboBox _account = new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230}; readonly TabControl _tabs = new(){Dock=DockStyle.Fill}; readonly Label _status = new(){Dock=DockStyle.Bottom,Height=28,TextAlign=ContentAlignment.MiddleLeft}; readonly System.Windows.Forms.Timer _timer = new();
    readonly TextBox _summary = new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Font=new Font("Microsoft YaHei UI",11)}; readonly TreeView _tree = new(){Dock=DockStyle.Fill,HideSelection=false,Font=new Font("Microsoft YaHei UI",10)};
    readonly DataGridView _mainGrid=Grid(), _nodeGrid=Grid(), _orderGrid=Grid(), _riskGrid=Grid(), _scenarioGrid=Grid(); bool _loadingUi;
    string AccountKey => _account.SelectedIndex==0 ? "BINANCE_UM" : "OKX_COIN";
    public MainForm()
    {
        Text="BTC交易管理系统 v0.3 主仓树"; Font=new Font("Microsoft YaHei UI",9); StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(1120,680); Size=new Size(1360,820); LoadUi(); BuildUi(); RefreshAll();
        _timer.Interval=Math.Max(1,_store.Config.RefreshSeconds)*1000; _timer.Tick+=(_,_)=>RefreshAll(); _timer.Start(); FormClosing+=(_,_)=>SaveUi(); ResizeEnd+=(_,_)=>SaveUi(); Move+=(_,_)=>SaveUi();
    }
    static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
    void LoadUi(){ var ui=_store.LoadUi(); _loadingUi=true; if(ui.X>=0&&ui.Y>=0){StartPosition=FormStartPosition.Manual; Location=new Point(ui.X,ui.Y);} Size=new Size(Math.Max(1120,ui.Width),Math.Max(680,ui.Height)); _loadingUi=false; }
    void SaveUi(){ if(_loadingUi||WindowState!=FormWindowState.Normal)return; _store.SaveUi(new UiSettings{X=Location.X,Y=Location.Y,Width=Width,Height=Height,SelectedTab=_tabs.SelectedIndex}); }
    void BuildUi()
    {
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1}; root.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Controls.Add(root); Controls.Add(_status);
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10,8,10,6),WrapContents=false}; root.Controls.Add(top,0,0); top.Controls.Add(new Label{Text="当前主仓：",AutoSize=true,Margin=new Padding(0,7,4,0)}); _account.Items.AddRange(new object[]{"Binance U本位主仓","OKX 币本位主仓"}); _account.SelectedIndex=1; _account.SelectedIndexChanged+=(_,_)=>RefreshAll(); top.Controls.Add(_account);
        AddButton(top,"刷新",RefreshAll); AddButton(top,"新增主仓",()=>EditMain(null)); AddButton(top,"新增节点",()=>EditNode(null)); AddButton(top,"归类订单",()=>EditOrder(null)); AddButton(top,"情景模拟",RunScenario); AddButton(top,"设置",OpenSettings);
        root.Controls.Add(_tabs,0,1);
        _tabs.TabPages.Add(Page("总控", _summary)); _tabs.TabPages.Add(Page("主仓管理", WithMenu(_mainGrid, MainMenu()))); _tabs.TabPages.Add(Page("主仓树", WithMenu(_tree, TreeMenu()))); _tabs.TabPages.Add(Page("节点明细", WithMenu(_nodeGrid, NodeMenu()))); _tabs.TabPages.Add(Page("订单映射/待归类", WithMenu(_orderGrid, OrderMenu()))); _tabs.TabPages.Add(Page("风险总控", _riskGrid)); _tabs.TabPages.Add(Page("情景模拟", _scenarioGrid));
        _mainGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditMain(RowId(_mainGrid)); }; _nodeGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditNode(RowId(_nodeGrid)); }; _orderGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditOrder(RowId(_orderGrid)); };
        _tree.NodeMouseDoubleClick += (_,e)=>{ if(e.Node.Tag is TagInfo t){ if(t.Kind=="main") EditMain(t.Id); else EditNode(t.Id); } };
    }
    static TabPage Page(string n, Control c){ var p=new TabPage(n); p.Controls.Add(c); return p; }
    void AddButton(FlowLayoutPanel p,string text,Action a){ var b=new Button{Text=text,Height=30,AutoSize=true,MinimumSize=new Size(82,30),Margin=new Padding(8,1,0,0)}; b.Click+=(_,_)=>a(); p.Controls.Add(b); }
    Control WithMenu(Control c, ContextMenuStrip m){ c.ContextMenuStrip=m; return c; }
    ContextMenuStrip MainMenu(){ var m=new ContextMenuStrip(); m.Items.Add("新增主仓",null,(_,_)=>EditMain(null)); m.Items.Add("修改主仓",null,(_,_)=>EditMain(RowId(_mainGrid))); m.Items.Add("删除主仓",null,(_,_)=>DeleteMain()); return m; }
    ContextMenuStrip NodeMenu(){ var m=new ContextMenuStrip(); m.Items.Add("新增节点",null,(_,_)=>EditNode(null)); m.Items.Add("修改节点",null,(_,_)=>EditNode(RowId(_nodeGrid))); m.Items.Add("删除节点",null,(_,_)=>DeleteNode()); return m; }
    ContextMenuStrip OrderMenu(){ var m=new ContextMenuStrip(); m.Items.Add("新增订单",null,(_,_)=>EditOrder(null)); m.Items.Add("修改订单",null,(_,_)=>EditOrder(RowId(_orderGrid))); m.Items.Add("删除订单",null,(_,_)=>DeleteOrder()); return m; }
    ContextMenuStrip TreeMenu(){ var m=new ContextMenuStrip(); m.Items.Add("新增主仓",null,(_,_)=>EditMain(null)); m.Items.Add("新增节点",null,(_,_)=>EditNode(null)); m.Items.Add("修改选中",null,(_,_)=>EditTreeSelected()); m.Items.Add("删除选中",null,(_,_)=>DeleteTreeSelected()); return m; }
    long? RowId(DataGridView g){ if(g.CurrentRow==null || !g.Columns.Contains("id")) return null; return Convert.ToInt64(g.CurrentRow.Cells["id"].Value); }
    void RefreshAll(){ LoadMainGrid(); LoadTree(); LoadNodeGrid(); LoadOrders(); LoadRisk(); LoadScenario(); BuildSummary(); }
    void LoadMainGrid(){ _mainGrid.DataSource=_store.Query("SELECT id,code AS 编号,direction AS 方向,cycle_name AS 周期,status AS 状态,base_qty AS 主仓数量,avg_price AS 主仓均价,margin AS 保证金,created_at AS 创建时间,note AS 备注 FROM main_positions WHERE account_key=$a ORDER BY id",("$a",AccountKey)); HideId(_mainGrid); }
    void LoadNodeGrid(){ _nodeGrid.DataSource=_store.Query("SELECT id,code AS 编号,node_type AS 类型,direction AS 方向,status AS 状态,qty AS 数量,entry_price AS 开仓价,close_price AS 平仓价,fee AS 手续费,funding AS 资金费,breakeven_price AS 盈亏平衡,cost_stop AS 成本止损,trailing_take_profit AS 移动止盈,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注 FROM position_nodes WHERE account_key=$a ORDER BY id",("$a",AccountKey)); HideId(_nodeGrid); }
    void LoadOrders(){ _orderGrid.DataSource=_store.Query("SELECT id,order_id AS 订单号,trade_id AS 成交号,side AS 方向,action AS 动作,price AS 价格,qty AS 数量,fee AS 手续费,funding AS 资金费,ts AS 时间,match_status AS 匹配状态,raw_note AS 备注 FROM order_mappings WHERE account_key=$a ORDER BY id DESC",("$a",AccountKey)); HideId(_orderGrid); }
    void HideId(DataGridView g){ if(g.Columns.Contains("id")) g.Columns["id"].Visible=false; }
    void LoadTree(){ _tree.BeginUpdate(); _tree.Nodes.Clear(); var mains=_store.Query("SELECT * FROM main_positions WHERE account_key=$a ORDER BY id",("$a",AccountKey)); foreach(DataRow m in mains.Rows){ var n=new TreeNode($"{m["code"]} 主仓｜{m["direction"]}｜{m["status"]}"){Tag=new TagInfo("main",Convert.ToInt64(m["id"]))}; _tree.Nodes.Add(n); var nodes=_store.Query("SELECT * FROM position_nodes WHERE account_key=$a AND main_code=$m ORDER BY id",("$a",AccountKey),("$m",Convert.ToString(m["code"])!)); foreach(DataRow r in nodes.Rows){ n.Nodes.Add(new TreeNode($"{r["code"]} {r["node_type"]}｜{r["direction"]}｜{r["status"]}｜{Fmt(r["qty"])}"){Tag=new TagInfo("node",Convert.ToInt64(r["id"]))}); } n.Expand(); } _tree.EndUpdate(); }
    void LoadRisk(){ var root=_store.LoadLatest(out _); var s=FindSnap(root); var dt=new DataTable(); foreach(var c in new[]{"项目","结果","说明"})dt.Columns.Add(c); dt.Rows.Add("账户",_account.Text,"Binance/OKX独立主仓"); dt.Rows.Add("采集状态",s?.Status??"无数据",s?.LastSuccess??""); dt.Rows.Add("实时价格",FmtStr(s?.Price,1),"latest_snapshot"); dt.Rows.Add("持仓",Pos(s?.Position),"交易所真实持仓"); dt.Rows.Add("强平距离",LiqDistance(s),"标记价到强平价"); dt.Rows.Add("节点数量",_store.Scalar("SELECT COUNT(*) FROM position_nodes WHERE account_key=$a",("$a",AccountKey))?.ToString()??"0","A/H/TP/SL/R"); dt.Rows.Add("待归类订单",_store.Scalar("SELECT COUNT(*) FROM order_mappings WHERE account_key=$a AND match_status='待归类'",("$a",AccountKey))?.ToString()??"0","需要手动处理"); _riskGrid.DataSource=dt; }
    void LoadScenario(){ _scenarioGrid.DataSource=_store.Query("SELECT id,target_price AS 目标价,created_at AS 时间,result_json AS 结果 FROM scenario_runs WHERE account_key=$a ORDER BY id DESC",("$a",AccountKey)); HideId(_scenarioGrid); }
    void BuildSummary(){ var root=_store.LoadLatest(out var err); var s=FindSnap(root); var sb=new StringBuilder(); sb.AppendLine($"当前主仓：{_account.Text}"); sb.AppendLine(new string('=',60)); if(root==null){sb.AppendLine("采集器数据未就绪："+err); _summary.Text=sb.ToString(); return;} sb.AppendLine($"采集器更新时间：{root.updated_at} schema {root.schema_version}"); sb.AppendLine($"状态：{s?.Status??"--"}  价格：{FmtStr(s?.Price,1)}  标记：{FmtStr(s?.Mark,1)}  强平：{FmtStr(s?.Liq,1)}"); sb.AppendLine($"权益：{FmtStr(s?.Equity,4)}  可用：{FmtStr(s?.Available,4)}  持仓：{Pos(s?.Position)}  浮盈亏：{FmtStr(s?.Upnl,4)}"); sb.AppendLine($"强平距离：{LiqDistance(s)}  资金费率：{s?.Funding??"--"}"); sb.AppendLine(); sb.AppendLine("页面说明：主仓管理、主仓树、节点明细、订单映射都是独立完整页面。右键可新增/修改/删除。窗口大小位置会自动保存。"); _summary.Text=sb.ToString(); _status.Text=$"{_account.Text} | {DateTime.Now:HH:mm:ss}"; }
    CollectorSnapshot? FindSnap(SnapshotRoot? root)=>root==null?null:(AccountKey=="BINANCE_UM"?root.snapshots.FirstOrDefault(x=>x.Exchange.StartsWith("Binance")):root.snapshots.FirstOrDefault(x=>x.Exchange.StartsWith("OKX")));

    void EditMain(long? id){ var row=id==null?null:_store.Query("SELECT * FROM main_positions WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault(); using var f=new MainEditForm(row); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; if(id==null)_store.Exec("INSERT OR IGNORE INTO main_positions(account_key,code,direction,cycle_name,status,base_qty,avg_price,margin,created_at,note) VALUES($a,$c,$d,$cy,$s,$q,$p,$m,datetime('now','localtime'),$n)",("$a",AccountKey),("$c",v.Code),("$d",v.Direction),("$cy",v.Cycle),("$s",v.Status),("$q",v.Qty),("$p",v.Price),("$m",v.Margin),("$n",v.Note)); else _store.Exec("UPDATE main_positions SET code=$c,direction=$d,cycle_name=$cy,status=$s,base_qty=$q,avg_price=$p,margin=$m,note=$n WHERE id=$id",("$c",v.Code),("$d",v.Direction),("$cy",v.Cycle),("$s",v.Status),("$q",v.Qty),("$p",v.Price),("$m",v.Margin),("$n",v.Note),("$id",id)); RefreshAll(); } }
    void EditNode(long? id){ var row=id==null?null:_store.Query("SELECT * FROM position_nodes WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault(); using var f=new NodeEditForm(row,_store.Config.DefaultFeeRatePct); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; if(id==null)_store.Exec("INSERT OR IGNORE INTO position_nodes(account_key,main_code,code,node_type,direction,status,qty,entry_price,close_price,fee,funding,breakeven_price,cost_stop,trailing_take_profit,exchange_open_order_id,exchange_close_order_id,created_at,note) VALUES($a,$m,$c,$t,$d,$s,$q,$e,$cl,$f,$fu,$b,$cs,$tp,$oo,$co,datetime('now','localtime'),$n)",("$a",AccountKey),("$m",v.MainCode),("$c",v.Code),("$t",v.Type),("$d",v.Direction),("$s",v.Status),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",v.Fee),("$fu",v.Funding),("$b",v.Breakeven),("$cs",v.CostStop),("$tp",v.TrailingTp),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$n",v.Note)); else _store.Exec("UPDATE position_nodes SET main_code=$m,code=$c,node_type=$t,direction=$d,status=$s,qty=$q,entry_price=$e,close_price=$cl,fee=$f,funding=$fu,breakeven_price=$b,cost_stop=$cs,trailing_take_profit=$tp,exchange_open_order_id=$oo,exchange_close_order_id=$co,note=$n WHERE id=$id",("$m",v.MainCode),("$c",v.Code),("$t",v.Type),("$d",v.Direction),("$s",v.Status),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",v.Fee),("$fu",v.Funding),("$b",v.Breakeven),("$cs",v.CostStop),("$tp",v.TrailingTp),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$n",v.Note),("$id",id)); RefreshAll(); } }
    void EditOrder(long? id){ var row=id==null?null:_store.Query("SELECT * FROM order_mappings WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault(); using var f=new OrderEditForm(row); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; if(id==null)_store.Exec("INSERT INTO order_mappings(account_key,order_id,trade_id,side,action,price,qty,fee,funding,ts,match_status,raw_note) VALUES($a,$o,$tr,$s,$ac,$p,$q,$f,$fu,$ts,$ms,$n)",("$a",AccountKey),("$o",v.OrderId),("$tr",v.TradeId),("$s",v.Side),("$ac",v.Action),("$p",v.Price),("$q",v.Qty),("$f",v.Fee),("$fu",v.Funding),("$ts",v.Ts),("$ms",v.Match),("$n",v.Note)); else _store.Exec("UPDATE order_mappings SET order_id=$o,trade_id=$tr,side=$s,action=$ac,price=$p,qty=$q,fee=$f,funding=$fu,ts=$ts,match_status=$ms,raw_note=$n WHERE id=$id",("$o",v.OrderId),("$tr",v.TradeId),("$s",v.Side),("$ac",v.Action),("$p",v.Price),("$q",v.Qty),("$f",v.Fee),("$fu",v.Funding),("$ts",v.Ts),("$ms",v.Match),("$n",v.Note),("$id",id)); RefreshAll(); } }
    void DeleteMain(){ var id=RowId(_mainGrid); if(id==null)return; if(MessageBox.Show(this,"删除主仓会同时删除该主仓节点，确定？","确认",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){ var code=Convert.ToString(_store.Scalar("SELECT code FROM main_positions WHERE id=$id",("$id",id)))??""; _store.Exec("DELETE FROM position_nodes WHERE account_key=$a AND main_code=$m",("$a",AccountKey),("$m",code)); _store.Exec("DELETE FROM main_positions WHERE id=$id",("$id",id)); RefreshAll(); } }
    void DeleteNode(){ var id=RowId(_nodeGrid); if(id==null)return; if(MessageBox.Show(this,"删除选中节点？","确认",MessageBoxButtons.YesNo)==DialogResult.Yes){ _store.Exec("DELETE FROM position_nodes WHERE id=$id",("$id",id)); RefreshAll(); } }
    void DeleteOrder(){ var id=RowId(_orderGrid); if(id==null)return; if(MessageBox.Show(this,"删除选中订单？","确认",MessageBoxButtons.YesNo)==DialogResult.Yes){ _store.Exec("DELETE FROM order_mappings WHERE id=$id",("$id",id)); RefreshAll(); } }
    void EditTreeSelected(){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main")EditMain(t.Id); else EditNode(t.Id); }
    void DeleteTreeSelected(){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main"){ SelectGridRow(_mainGrid,t.Id); DeleteMain(); } else { SelectGridRow(_nodeGrid,t.Id); DeleteNode(); } }
    void SelectGridRow(DataGridView g,long id){ foreach(DataGridViewRow r in g.Rows) if(Convert.ToInt64(r.Cells["id"].Value)==id){ g.CurrentCell=r.Cells.Cast<DataGridViewCell>().First(c=>c.Visible); break; } }
    void RunScenario(){ using var f=new SimpleComboForm("情景模拟", new FieldSpec("目标BTC价格","text")); if(f.ShowDialog(this)==DialogResult.OK){ var target=Dec(f.Values[0]); var root=_store.LoadLatest(out _); var s=FindSnap(root); var mark=Dec(s?.Mark); var result=$"当前标记价 {mark:F1}，目标价 {target:F1}，价差 {target-mark:F1}。后续将按每个A/H分支逐项计算。"; _store.Exec("INSERT INTO scenario_runs(account_key,target_price,created_at,result_json) VALUES($a,$p,datetime('now','localtime'),$r)",("$a",AccountKey),("$p",target),("$r",result)); RefreshAll(); MessageBox.Show(this,result,"情景模拟"); } }
    void OpenSettings(){ using var f=new SettingsForm(_store.Config); if(f.ShowDialog(this)==DialogResult.OK){ _store.SaveConfig(f.Config); _timer.Interval=Math.Max(1,_store.Config.RefreshSeconds)*1000; RefreshAll(); } }
    static decimal Dec(object? s)=>decimal.TryParse(Convert.ToString(s),NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; static object? DbNum(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:DBNull.Value; static string Fmt(object? o)=>Dec(o).ToString("F4"); static string FmtStr(string? s,int n)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d.ToString("F"+n):"--"; static string Pos(string? s)=>string.IsNullOrWhiteSpace(s)||s=="--"?"--":s.Replace("short ","空").Replace("long ","多"); static string LiqDistance(CollectorSnapshot? s){var m=Dec(s?.Mark);var l=Dec(s?.Liq);return m>0&&l>0?(Math.Abs(l-m)/m*100m).ToString("F2")+"%":"--";} record TagInfo(string Kind,long Id);
}

public record FieldSpec(string Label,string Kind,string[]? Options=null,string Default="");
public class SimpleComboForm:Form
{
    readonly List<Control> _inputs=new(); public string[] Values=>_inputs.Select(c=>c is ComboBox cb?Convert.ToString(cb.SelectedItem)??cb.Text:c.Text).ToArray();
    public SimpleComboForm(string title, params FieldSpec[] fields){ Text=title; StartPosition=FormStartPosition.CenterParent; MinimizeBox=false; MaximizeBox=false; FormBorderStyle=FormBorderStyle.FixedDialog; Font=new Font("Microsoft YaHei UI",9); Width=720; Height=Math.Min(760,120+fields.Length*42); var p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=2,RowCount=fields.Length+1}; p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(p); int r=0; foreach(var f in fields){ p.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); p.Controls.Add(new Label{Text=f.Label,Anchor=AnchorStyles.Left,AutoSize=true},0,r); Control input; if(f.Kind=="combo"||f.Options!=null){ var cb=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList}; cb.Items.AddRange(f.Options??Array.Empty<string>()); if(!string.IsNullOrWhiteSpace(f.Default)&&cb.Items.Contains(f.Default)) cb.SelectedItem=f.Default; else if(cb.Items.Count>0) cb.SelectedIndex=0; input=cb; } else input=new TextBox{Dock=DockStyle.Fill,Text=f.Default}; _inputs.Add(input); p.Controls.Add(input,1,r++); } var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,8,0,0)}; var save=new Button{Text="保存",Width=100,Height=32,DialogResult=DialogResult.OK}; var cancel=new Button{Text="取消",Width=100,Height=32,DialogResult=DialogResult.Cancel}; bottom.Controls.Add(save); bottom.Controls.Add(cancel); p.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); p.Controls.Add(bottom,0,r); p.SetColumnSpan(bottom,2); AcceptButton=save; CancelButton=cancel; }
}
public sealed class MainEditForm:SimpleComboForm{ public MainValue V=>new(Values[0],Values[1],Values[2],Values[3],Dec(Values[4]),Dec(Values[5]),Dec(Values[6]),Values[7]); public MainEditForm(DataRow? r):base(r==null?"新增主仓":"修改主仓", new FieldSpec("编号","combo",Enumerable.Range(1,10).Select(i=>$"M{i}").ToArray(),S(r,"code","M1")), new FieldSpec("方向","combo",new[]{"多","空"},S(r,"direction","多")), new FieldSpec("周期名称","text",Default:S(r,"cycle_name","当前牛熊周期")), new FieldSpec("状态","combo",new[]{"规划中","持仓中","已完成","已取消"},S(r,"status","规划中")), new FieldSpec("主仓数量","text",Default:S(r,"base_qty","0")), new FieldSpec("主仓均价","text",Default:S(r,"avg_price","0")), new FieldSpec("保证金","text",Default:S(r,"margin","0")), new FieldSpec("备注","text",Default:S(r,"note",""))){} static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record MainValue(string Code,string Direction,string Cycle,string Status,decimal Qty,decimal Price,decimal Margin,string Note);
public sealed class NodeEditForm:SimpleComboForm{ public NodeValue V=>new(Values[0],Values[1],Values[2],Values[3],Values[4],Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Dec(Values[8]),Dec(Values[9]),Dec(Values[10]),Dec(Values[11]),Dec(Values[12]),Values[13],Values[14],Values[15]); public NodeEditForm(DataRow? r,decimal feeRate):base(r==null?"新增节点":"修改节点", new FieldSpec("主仓编号","combo",Enumerable.Range(1,10).Select(i=>$"M{i}").ToArray(),S(r,"main_code","M1")), new FieldSpec("节点编号","combo",new[]{"A1","A2","A3","A4","A5","H1","H2","H3","TP1","TP2","TP3","SL1","SL2","R1","R2"},S(r,"code","A1")), new FieldSpec("类型","combo",new[]{"加仓","对冲","止盈","止损","减仓"},S(r,"node_type","加仓")), new FieldSpec("方向","combo",new[]{"多","空"},S(r,"direction","多")), new FieldSpec("状态","combo",new[]{"计划中","已开仓","已平仓","已取消","待归类"},S(r,"status","计划中")), new FieldSpec("数量","text",Default:S(r,"qty","0")), new FieldSpec("开仓价","text",Default:S(r,"entry_price","0")), new FieldSpec("平仓价","text",Default:S(r,"close_price","0")), new FieldSpec("手续费","text",Default:S(r,"fee","0")), new FieldSpec("资金费","text",Default:S(r,"funding","0")), new FieldSpec("盈亏平衡","text",Default:S(r,"breakeven_price","0")), new FieldSpec("成本止损","text",Default:S(r,"cost_stop","0")), new FieldSpec("移动止盈","text",Default:S(r,"trailing_take_profit","0")), new FieldSpec("开仓订单号","text",Default:S(r,"exchange_open_order_id","")), new FieldSpec("平仓订单号","text",Default:S(r,"exchange_close_order_id","")), new FieldSpec("备注","text",Default:S(r,"note",""))){} static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record NodeValue(string MainCode,string Code,string Type,string Direction,string Status,decimal Qty,decimal Entry,decimal Close,decimal Fee,decimal Funding,decimal Breakeven,decimal CostStop,decimal TrailingTp,string OpenOrder,string CloseOrder,string Note);
public sealed class OrderEditForm:SimpleComboForm{ public OrderValue V=>new(Values[0],Values[1],Values[2],Values[3],Dec(Values[4]),Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Values[8],Values[9],Values[10]); public OrderEditForm(DataRow? r):base(r==null?"新增订单":"修改订单", new FieldSpec("订单号","text",Default:S(r,"order_id","")), new FieldSpec("成交号","text",Default:S(r,"trade_id","")), new FieldSpec("方向","combo",new[]{"多","空","买入","卖出"},S(r,"side","多")), new FieldSpec("动作","combo",new[]{"开仓","平仓","加仓","对冲","止盈","止损","减仓","资金费"},S(r,"action","开仓")), new FieldSpec("价格","text",Default:S(r,"price","0")), new FieldSpec("数量","text",Default:S(r,"qty","0")), new FieldSpec("手续费","text",Default:S(r,"fee","0")), new FieldSpec("资金费","text",Default:S(r,"funding","0")), new FieldSpec("时间","text",Default:S(r,"ts",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))), new FieldSpec("匹配状态","combo",new[]{"已匹配","待归类","忽略"},S(r,"match_status","待归类")), new FieldSpec("备注","text",Default:S(r,"raw_note",""))){} static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record OrderValue(string OrderId,string TradeId,string Side,string Action,decimal Price,decimal Qty,decimal Fee,decimal Funding,string Ts,string Match,string Note);
public sealed class SettingsForm:SimpleComboForm{ public AppConfig Config{get;} public SettingsForm(AppConfig cfg):base("设置", new FieldSpec("采集器latest路径","text",Default:cfg.CollectorLatestPath), new FieldSpec("管理数据目录","text",Default:cfg.DataDir), new FieldSpec("刷新秒数","combo",new[]{"1","2","3","5","10","30","60"},cfg.RefreshSeconds.ToString()), new FieldSpec("默认单向手续费%","combo",new[]{"0.05","0.04","0.03","0.02","0.01"},cfg.DefaultFeeRatePct.ToString(CultureInfo.InvariantCulture))){ FormClosing+=(_,_)=>{}; Config=new AppConfig{CollectorLatestPath=Values.Length>0?Values[0]:cfg.CollectorLatestPath,DataDir=Values.Length>1?Values[1]:cfg.DataDir,RefreshSeconds=cfg.RefreshSeconds,DefaultFeeRatePct=cfg.DefaultFeeRatePct,MaxSingleRiskPct=cfg.MaxSingleRiskPct}; } protected override void OnFormClosing(FormClosingEventArgs e){ base.OnFormClosing(e); if(DialogResult==DialogResult.OK){ var vals=Values; Config.CollectorLatestPath=vals[0]; Config.DataDir=vals[1]; Config.RefreshSeconds=(int)(decimal.TryParse(vals[2],out var s)?s:3); Config.DefaultFeeRatePct=decimal.TryParse(vals[3],NumberStyles.Any,CultureInfo.InvariantCulture,out var f)?f:0.05m; } } }
