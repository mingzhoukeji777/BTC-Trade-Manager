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
        using var mutex = new Mutex(true, "Global\\BTCTradeManager_XiaoZhu_v06", out var created);
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
    public Dictionary<string, Dictionary<string,int>> GridWidths { get; set; } = new();
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
        foreach(var alter in new[]{
            "ALTER TABLE main_positions ADD COLUMN hard_stop REAL",
            "ALTER TABLE main_positions ADD COLUMN fee REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN breakeven_price REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN exchange_open_order_id TEXT",
            "ALTER TABLE main_positions ADD COLUMN exchange_close_order_id TEXT",
            "ALTER TABLE main_positions ADD COLUMN close_price REAL",
            "ALTER TABLE main_positions ADD COLUMN cost_stop REAL",
            "ALTER TABLE main_positions ADD COLUMN trailing_take_profit REAL",
            "ALTER TABLE main_positions ADD COLUMN liq_price REAL",
            "ALTER TABLE main_positions ADD COLUMN floating_pnl REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN maintenance_margin_rate REAL",
            "ALTER TABLE main_positions ADD COLUMN funding_rate REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN cumulative_funding REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN cumulative_fee REAL DEFAULT 0",
            "ALTER TABLE main_positions ADD COLUMN close_time TEXT",
            "ALTER TABLE main_positions ADD COLUMN close_type TEXT",
            "ALTER TABLE position_nodes ADD COLUMN stop_loss REAL",
            "ALTER TABLE position_nodes ADD COLUMN close_exit_price REAL",
            "ALTER TABLE position_nodes ADD COLUMN liq_price REAL",
            "ALTER TABLE position_nodes ADD COLUMN floating_pnl REAL DEFAULT 0",
            "ALTER TABLE position_nodes ADD COLUMN maintenance_margin_rate REAL",
            "ALTER TABLE position_nodes ADD COLUMN funding_rate REAL DEFAULT 0",
            "ALTER TABLE position_nodes ADD COLUMN cumulative_funding REAL DEFAULT 0",
            "ALTER TABLE position_nodes ADD COLUMN cumulative_fee REAL DEFAULT 0",
            "ALTER TABLE position_nodes ADD COLUMN close_time TEXT",
            "ALTER TABLE position_nodes ADD COLUMN close_type TEXT",
            "ALTER TABLE position_nodes ADD COLUMN reduce_source_code TEXT"
        }){ try{ using var ac=cn.CreateCommand(); ac.CommandText=alter; ac.ExecuteNonQuery(); }catch{} }

        try{
            using var up1=cn.CreateCommand(); up1.CommandText="UPDATE main_positions SET status='持有中' WHERE status IN ('已开仓','持仓中')"; up1.ExecuteNonQuery();
            using var up2=cn.CreateCommand(); up2.CommandText="UPDATE position_nodes SET status='持有中' WHERE status IN ('已开仓','持仓中')"; up2.ExecuteNonQuery();
        }catch{}
    }
    public DataTable Query(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); using var r=cmd.ExecuteReader(); var dt=new DataTable(); dt.Load(r); return dt; }
    public void Exec(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); cmd.ExecuteNonQuery(); }
    public object? Scalar(string sql, params (string,object?)[] args){ InitDb(); using var cn=new SqliteConnection($"Data Source={DbPath}"); cn.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText=sql; foreach(var (k,v) in args) cmd.Parameters.AddWithValue(k,v??DBNull.Value); return cmd.ExecuteScalar(); }
    public SnapshotRoot? LoadLatest(out string err){ err=""; try{ if(!File.Exists(Config.CollectorLatestPath)){ err="未找到采集器 latest_snapshot.json"; return null; } return JsonSerializer.Deserialize<SnapshotRoot>(File.ReadAllText(Config.CollectorLatestPath,Encoding.UTF8), new JsonSerializerOptions{PropertyNameCaseInsensitive=true}); }catch(Exception ex){ err=ex.Message; return null; } }
}

public sealed class MainForm:Form
{
    readonly Store _store = new(); readonly ComboBox _account = new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230}; readonly TabControl _tabs = new(){Dock=DockStyle.Fill}; readonly Label _status = new(){Dock=DockStyle.Bottom,Height=28,TextAlign=ContentAlignment.MiddleLeft}; readonly System.Windows.Forms.Timer _timer = new();
    readonly DataGridView _summary = Grid(); readonly TreeView _tree = new(){Dock=DockStyle.Fill,HideSelection=false,Font=new Font("Microsoft YaHei",10)};
    readonly DataGridView _mainGrid=Grid(), _nodeGrid=Grid(), _addGrid=Grid(), _hedgeGrid=Grid(), _reduceGrid=Grid(), _closedGrid=Grid(), _orderGrid=Grid(), _riskGrid=Grid(), _scenarioGrid=Grid(); bool _loadingUi; bool _applyingLayout;
    string AccountKey => _account.SelectedIndex==0 ? "BINANCE_UM" : "OKX_COIN";
    public MainForm()
    {
        Text="BTC交易管理系统 v0.7.3"; Font=new Font("Microsoft YaHei",9); StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(1120,680); Size=new Size(1360,820); LoadUi(); BuildUi(); RefreshAll();
        _timer.Interval=Math.Max(1,_store.Config.RefreshSeconds)*1000; _timer.Tick+=(_,_)=>RefreshLiveOnly(); _timer.Start(); FormClosing+=(_,_)=>SaveUi(); ResizeEnd+=(_,_)=>SaveUi(); Move+=(_,_)=>SaveUi();
    }
    static DataGridView Grid(){
        var g=new DataGridView{Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei",11),DefaultCellStyle=new DataGridViewCellStyle{Font=new Font("Microsoft YaHei",11),Alignment=DataGridViewContentAlignment.MiddleCenter},ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{Font=new Font("Microsoft YaHei",11,FontStyle.Bold),Alignment=DataGridViewContentAlignment.MiddleCenter,WrapMode=DataGridViewTriState.False},ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoGenerateColumns=true,BackgroundColor=Color.White,GridColor=Color.LightGray,EnableHeadersVisualStyles=false,ColumnHeadersHeight=36,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
        g.RowTemplate.Height=34; g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(255,245,180); g.DefaultCellStyle.SelectionForeColor=Color.Black; g.RowTemplate.DefaultCellStyle.SelectionBackColor=Color.FromArgb(255,245,180); g.RowTemplate.DefaultCellStyle.SelectionForeColor=Color.Black; g.DataError += (_,e)=>{ e.ThrowException=false; e.Cancel=true; };
        g.ColumnDividerDoubleClick += (sender,e)=>{ var dg=(DataGridView)sender!; if(e.ColumnIndex>=0){ dg.EnableHeadersVisualStyles=false; dg.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; dg.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; var col=dg.Columns[e.ColumnIndex]; var w=TextRenderer.MeasureText(col.HeaderText??col.Name,dg.ColumnHeadersDefaultCellStyle.Font??dg.Font).Width+28; col.Width=Math.Max(48,Math.Min(220,w)); col.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; col.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter; col.HeaderCell.Style.Font=dg.ColumnHeadersDefaultCellStyle.Font; foreach(DataGridViewColumn c in dg.Columns){ c.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter; c.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; } foreach(DataGridViewRow row in dg.Rows){ try{ row.Cells[e.ColumnIndex].Style.Alignment=DataGridViewContentAlignment.MiddleCenter; }catch{} } try{ if(dg.FindForm() is MainForm mf) mf.SaveUi(); }catch{} } }; g.MouseClick += (sender,e)=>{ var dg=(DataGridView)sender!; var hit=dg.HitTest(e.X,e.Y); if(hit.Type==DataGridViewHitTestType.None || hit.RowIndex<0){ try{ dg.ClearSelection(); dg.CurrentCell=null; }catch{} } };
        return g;
    }
    void LoadUi(){ var ui=_store.LoadUi(); _loadingUi=true; if(ui.X>=0&&ui.Y>=0){StartPosition=FormStartPosition.Manual; Location=new Point(ui.X,ui.Y);} Size=new Size(Math.Max(1120,ui.Width),Math.Max(680,ui.Height)); _loadingUi=false; }
    void SaveUi(){ if(_loadingUi||WindowState!=FormWindowState.Normal)return; var ui=new UiSettings{X=Location.X,Y=Location.Y,Width=Width,Height=Height,SelectedTab=_tabs.SelectedIndex}; foreach(var g in new[]{_summary,_mainGrid,_addGrid,_hedgeGrid,_reduceGrid,_closedGrid,_orderGrid,_riskGrid,_scenarioGrid}){ if(string.IsNullOrWhiteSpace(g.Name))continue; ui.GridWidths[g.Name]=g.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).ToDictionary(c=>c.Name,c=>c.Width); } _store.SaveUi(ui); }
    void BuildUi()
    {
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1}; root.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Controls.Add(root); Controls.Add(_status); NameGrids(); _tree.ImageList=BuildStatusImages(); _tree.DrawMode=TreeViewDrawMode.OwnerDrawText; _tree.DrawNode+=DrawTreeNode; _tabs.DrawMode=TabDrawMode.OwnerDrawFixed; _tabs.Padding=new Point(18,4); _tabs.DrawItem+=DrawTab; _tabs.SelectedIndexChanged+=(_,_)=>{};
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10,8,10,6),WrapContents=false}; root.Controls.Add(top,0,0); top.Controls.Add(new Label{Text="当前主仓：",AutoSize=true,Margin=new Padding(0,7,4,0)}); _account.Items.AddRange(new object[]{"Binance U本位主仓","OKX 币本位主仓"}); _account.SelectedIndex=1; _account.SelectedIndexChanged+=(_,_)=>RefreshAll(); top.Controls.Add(_account);
        AddButton(top,"刷新",RefreshAll); AddButton(top,"新增主仓",()=>EditMain(null)); AddButton(top,"加仓",()=>EditNodePreset("加仓")); AddButton(top,"对冲",()=>EditNodePreset("对冲")); AddButton(top,"归类订单",()=>EditOrder(null)); AddButton(top,"情景模拟",RunScenario); AddButton(top,"设置",OpenSettings);
        root.Controls.Add(_tabs,0,1);
        _tabs.TabPages.Add(Page("总控", _summary)); _tabs.TabPages.Add(Page("主仓M", WithMenu(_mainGrid, MainMenu()))); _tabs.TabPages.Add(Page("主仓树", WithMenu(_tree, TreeMenu()))); _tabs.TabPages.Add(Page("加仓A", WithMenu(_addGrid, NodeMenu()))); _tabs.TabPages.Add(Page("对冲H", WithMenu(_hedgeGrid, NodeMenu()))); _tabs.TabPages.Add(Page("减仓R", WithMenu(_reduceGrid, NodeMenu()))); _tabs.TabPages.Add(Page("已平仓", WithMenu(_closedGrid, NodeMenu()))); _tabs.TabPages.Add(Page("订单映射/待归类", WithMenu(_orderGrid, OrderMenu()))); _tabs.TabPages.Add(Page("风险总控", _riskGrid)); _tabs.TabPages.Add(Page("情景模拟", _scenarioGrid));
        _mainGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditMain(RowId(_mainGrid)); }; _nodeGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditNode(RowId(_nodeGrid)); }; _addGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditNode(RowId(_addGrid)); }; _hedgeGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditNode(RowId(_hedgeGrid)); }; _reduceGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditNode(RowId(_reduceGrid)); }; _closedGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditClosed(RowId(_closedGrid)); }; _orderGrid.CellDoubleClick += (_,e)=>{ if(e.RowIndex>=0) EditOrder(RowId(_orderGrid)); }; foreach(var dg in new[]{_summary,_mainGrid,_addGrid,_hedgeGrid,_reduceGrid,_closedGrid,_orderGrid,_riskGrid,_scenarioGrid}) dg.ColumnWidthChanged+=(_,_)=>{ if(!_loadingUi && !_applyingLayout) SaveUi(); };
    }
    void NameGrids(){ _summary.Name="总控"; _summary.Font=new Font("Microsoft YaHei",14,FontStyle.Regular); _summary.DefaultCellStyle.Font=new Font("Microsoft YaHei",14,FontStyle.Regular); _summary.ColumnHeadersDefaultCellStyle.Font=new Font("Microsoft YaHei",13,FontStyle.Bold); _summary.RowTemplate.Height=42; _summary.RowTemplate.DefaultCellStyle.Font=new Font("Microsoft YaHei",14,FontStyle.Regular); _summary.DefaultCellStyle.WrapMode=DataGridViewTriState.True; _summary.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; _summary.BackgroundColor=Color.White; _mainGrid.Name="主仓M"; _addGrid.Name="加仓A"; _hedgeGrid.Name="对冲H"; _reduceGrid.Name="减仓R"; _closedGrid.Name="已平仓"; _orderGrid.Name="订单"; _riskGrid.Name="风险"; _scenarioGrid.Name="情景"; }
    ImageList BuildStatusImages(){ var il=new ImageList{ImageSize=new Size(16,16),ColorDepth=ColorDepth.Depth32Bit}; il.Images.Add("red",Circle(Color.Red)); il.Images.Add("yellow",Circle(Color.Gold)); il.Images.Add("green",Circle(Color.LimeGreen)); il.Images.Add("gray",Circle(Color.Gray)); return il; }
    Bitmap Circle(Color c){ var bmp=new Bitmap(16,16); using var g=Graphics.FromImage(bmp); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var b=new SolidBrush(c); using var p=new Pen(Color.Black,1); g.FillEllipse(b,2,2,12,12); g.DrawEllipse(p,2,2,12,12); return bmp; }
    string StatusKey(string? s)=>(s=="持有中"||s=="持仓中")?"red":s=="成本止损"?"yellow":s=="移动止盈"?"green":"gray";
    void DrawTreeNode(object? sender, DrawTreeNodeEventArgs e){ var selected=(e.State&TreeNodeStates.Selected)!=0; var bounds=new Rectangle(e.Bounds.X,e.Bounds.Y,Math.Max(e.Bounds.Width,260),e.Bounds.Height); using var bg=new SolidBrush(selected?Color.FromArgb(255,245,180):_tree.BackColor); e.Graphics.FillRectangle(bg,bounds); TextRenderer.DrawText(e.Graphics,e.Node?.Text??"",_tree.Font,bounds,Color.Black,TextFormatFlags.VerticalCenter|TextFormatFlags.Left); if(selected) using(var pen=new Pen(Color.FromArgb(180,120,0),1)){ e.Graphics.DrawRectangle(pen,bounds.X,bounds.Y,bounds.Width-1,bounds.Height-1); } }
    static TabPage Page(string n, Control c){ var p=new TabPage(n); p.Controls.Add(c); return p; }
    void AddButton(FlowLayoutPanel p,string text,Action a){ var b=new Button{Text=text,Height=30,AutoSize=true,MinimumSize=new Size(82,30),Margin=new Padding(8,1,0,0)}; b.Click+=(_,_)=>a(); p.Controls.Add(b); }
    void DrawTab(object? sender, DrawItemEventArgs e){ var tab=_tabs.TabPages[e.Index]; var r=e.Bounds; using var back=new SolidBrush(e.State.HasFlag(DrawItemState.Selected)?Color.White:Color.FromArgb(238,238,238)); e.Graphics.FillRectangle(back,r); e.Graphics.DrawRectangle(Pens.Gray,r.X,r.Y,r.Width-1,r.Height-1); using var tf=new Font("Microsoft YaHei",10,FontStyle.Regular); TextRenderer.DrawText(e.Graphics,tab.Text,tf,r,Color.Black,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); }
    Control WithMenu(Control c, ContextMenuStrip m){ c.ContextMenuStrip=m; return c; }
    ContextMenuStrip MainMenu(){ var m=new ContextMenuStrip(); m.Items.Add("修改",null,(_,_)=>EditMain(RowId(_mainGrid))); m.Items.Add("删除",null,(_,_)=>DeleteMain()); m.Items.Add("移动止盈",null,(_,_)=>SetMainProtection("移动止盈")); m.Items.Add("成本止损",null,(_,_)=>SetMainProtection("成本止损")); m.Items.Add("平仓",null,(_,_)=>CloseMain()); m.Items.Add("减仓",null,(_,_)=>ReduceMain()); return m; }
    ContextMenuStrip NodeMenu(){ var m=new ContextMenuStrip(); m.Items.Add("修改",null,(_,_)=>EditCurrentOrClosed()); m.Items.Add("删除",null,(_,_)=>DeleteNode()); m.Items.Add("移动止盈",null,(_,_)=>SetProtection("移动止盈")); m.Items.Add("成本止损",null,(_,_)=>SetProtection("成本止损")); m.Items.Add("平仓",null,(_,_)=>CloseNode()); m.Items.Add("减仓",null,(_,_)=>ReduceCurrentNode()); return m; }
    ContextMenuStrip OrderMenu(){ var m=new ContextMenuStrip(); m.Items.Add("新增订单",null,(_,_)=>EditOrder(null)); m.Items.Add("修改订单",null,(_,_)=>EditOrder(RowId(_orderGrid))); m.Items.Add("删除订单",null,(_,_)=>DeleteOrder()); return m; }
    ContextMenuStrip TreeMenu(){ var m=new ContextMenuStrip(); m.Items.Add("修改",null,(_,_)=>EditTreeSelected()); m.Items.Add("删除",null,(_,_)=>DeleteTreeSelected()); m.Items.Add("移动止盈",null,(_,_)=>SetTreeProtection("移动止盈")); m.Items.Add("成本止损",null,(_,_)=>SetTreeProtection("成本止损")); m.Items.Add("平仓",null,(_,_)=>CloseTreeSelected()); return m; }
    long? RowId(DataGridView g){ if(g.CurrentRow==null || !g.Columns.Contains("id")) return null; return Convert.ToInt64(g.CurrentRow.Cells["id"].Value); }
    Dictionary<string,Dictionary<string,int>> CaptureWidths(){
        var d=new Dictionary<string,Dictionary<string,int>>();
        foreach(var g in new[]{_summary,_mainGrid,_addGrid,_hedgeGrid,_reduceGrid,_closedGrid,_orderGrid,_riskGrid,_scenarioGrid}){
            if(string.IsNullOrWhiteSpace(g.Name)||g.Columns.Count==0) continue;
            d[g.Name]=g.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).ToDictionary(c=>c.Name,c=>c.Width);
        }
        return d;
    }
    void RestoreWidths(Dictionary<string,Dictionary<string,int>> d){
        try{
            _applyingLayout=true;
            foreach(var g in new[]{_summary,_mainGrid,_addGrid,_hedgeGrid,_reduceGrid,_closedGrid,_orderGrid,_riskGrid,_scenarioGrid}){
                if(string.IsNullOrWhiteSpace(g.Name)||!d.TryGetValue(g.Name,out var map)) continue;
                foreach(DataGridViewColumn c in g.Columns){ if(map.TryGetValue(c.Name,out var w)) try{ c.Width=Math.Max(45,Math.Min(1200,w)); }catch{} c.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; c.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter; }
            }
        }catch{} finally{ _applyingLayout=false; SaveUi(); }
    }
    void RefreshKeepWidths(){ var w=CaptureWidths(); RefreshAll(); RestoreWidths(w); }
    void RefreshAll(){ LoadMainGrid(); LoadTree(); LoadNodeGrid(); LoadOrders(); LoadRisk(); LoadScenario(); BuildSummary(); }
    void RefreshLiveOnly(){ LoadRisk(); if(_tabs.SelectedTab?.Text!="总控") BuildSummary(); else _status.Text=$"{_account.Text} | {DateTime.Now:HH:mm:ss}"; }
    void LoadMainGrid(){ _mainGrid.DataSource=_store.Query("SELECT id,code AS 编号,direction AS 方向,CASE cycle_name WHEN '牛市周期' THEN '牛' WHEN '熊市周期' THEN '熊' ELSE cycle_name END AS 周期,status AS 状态,base_qty AS 持仓量,avg_price AS 开仓均价,breakeven_price AS 盈亏平衡价,liq_price AS 强平价格,floating_pnl AS 浮动收益,maintenance_margin_rate AS 维持保证金率,margin AS 保证金,hard_stop AS 止损价格,cost_stop AS 成本止损,trailing_take_profit AS 移动止盈,funding_rate AS 资金费率,fee AS 手续费,cumulative_funding AS 累计资金费,cumulative_fee AS 累计手续费,close_price AS 平仓价格,created_at AS 开仓时间,close_time AS 平仓时间,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注 FROM main_positions WHERE account_key=$a AND status<>'已平仓' ORDER BY id",("$a",AccountKey)); HideId(_mainGrid); }
    void LoadNodeGrid(){
        var cols="SELECT id,main_code||'-'||code AS 编号,direction AS 方向,status AS 状态,qty AS 持仓量,entry_price AS 开仓均价,breakeven_price AS 盈亏平衡价,liq_price AS 强平价格,floating_pnl AS 浮动收益,maintenance_margin_rate AS 维持保证金率,0 AS 保证金,COALESCE(stop_loss,close_price) AS 止损价格,cost_stop AS 成本止损,trailing_take_profit AS 移动止盈,funding_rate AS 资金费率,fee AS 手续费,close_exit_price AS 平仓价格,created_at AS 开仓时间,close_time AS 平仓时间,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注 FROM position_nodes WHERE account_key=$a";
        _nodeGrid.DataSource=_store.Query(cols+" ORDER BY id",("$a",AccountKey)); HideId(_nodeGrid);
        _addGrid.DataSource=_store.Query(cols+" AND node_type='加仓' AND status<>'已平仓' ORDER BY id",("$a",AccountKey)); HideId(_addGrid);
        _hedgeGrid.DataSource=_store.Query(cols+" AND node_type='对冲' AND status<>'已平仓' ORDER BY id",("$a",AccountKey)); HideId(_hedgeGrid);
        var reduce="SELECT id,main_code||'-'||code AS 编号,COALESCE(main_code||'-'||reduce_source_code,reduce_source_code) AS 减仓订单编号,status AS 状态,qty AS 减仓量,CASE WHEN floating_pnl>=0 THEN '盈' ELSE '亏' END AS 盈亏,floating_pnl AS 盈亏金额,entry_price AS 开仓均价,close_exit_price AS 减仓价格,fee AS 手续费,created_at AS 开仓时间,close_time AS 平仓时间,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注 FROM position_nodes WHERE account_key=$a AND node_type='减仓' ORDER BY id DESC";
        _reduceGrid.DataSource=_store.Query(reduce,("$a",AccountKey)); HideId(_reduceGrid);
        var closed="SELECT id,编号,方向,状态,类型,平仓类型,持仓量,盈亏,盈亏金额,开仓均价,平仓价格,手续费,开仓时间,平仓时间,开仓订单号,平仓订单号,备注 FROM (SELECT id,code AS 编号,direction AS 方向,status AS 状态,'主仓' AS 类型,close_type AS 平仓类型,base_qty AS 持仓量,CASE WHEN floating_pnl>=0 THEN '盈' ELSE '亏' END AS 盈亏,floating_pnl AS 盈亏金额,avg_price AS 开仓均价,close_price AS 平仓价格,fee AS 手续费,created_at AS 开仓时间,close_time AS 平仓时间,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注,close_time AS sort_time FROM main_positions WHERE account_key=$a AND status='已平仓' UNION ALL SELECT id,main_code||'-'||code AS 编号,direction AS 方向,status AS 状态,node_type AS 类型,close_type AS 平仓类型,qty AS 持仓量,CASE WHEN floating_pnl>=0 THEN '盈' ELSE '亏' END AS 盈亏,floating_pnl AS 盈亏金额,entry_price AS 开仓均价,close_exit_price AS 平仓价格,fee AS 手续费,created_at AS 开仓时间,close_time AS 平仓时间,exchange_open_order_id AS 开仓订单号,exchange_close_order_id AS 平仓订单号,note AS 备注,close_time AS sort_time FROM position_nodes WHERE account_key=$a AND status='已平仓') ORDER BY CASE WHEN sort_time IS NULL OR sort_time='' THEN 0 ELSE 1 END DESC, datetime(sort_time) DESC, id DESC";
        _closedGrid.DataSource=_store.Query(closed,("$a",AccountKey)); HideId(_closedGrid);
    }
    void LoadOrders(){ _orderGrid.DataSource=_store.Query("SELECT id,order_id AS 订单号,trade_id AS 成交号,side AS 方向,action AS 动作,price AS 价格,qty AS 数量,fee AS 手续费,funding AS 资金费,ts AS 时间,match_status AS 匹配状态,raw_note AS 备注 FROM order_mappings WHERE account_key=$a ORDER BY id DESC",("$a",AccountKey)); HideId(_orderGrid); }
    void HideId(DataGridView g){
        g.DataError -= Grid_DataError; g.DataError += Grid_DataError;
        for(int i=0;i<g.Columns.Count;i++){
            if(g.Columns[i] is DataGridViewImageColumn img){
                var txt=new DataGridViewTextBoxColumn{ Name=img.Name, HeaderText=img.HeaderText, DataPropertyName=img.DataPropertyName, Visible=img.Visible, FillWeight=img.FillWeight, AutoSizeMode=img.AutoSizeMode};
                g.Columns.RemoveAt(i); g.Columns.Insert(i,txt);
            }
            g.Columns[i].ValueType=typeof(string);
        }
        if(g.Columns.Contains("id")) g.Columns["id"].Visible=false; g.EnableHeadersVisualStyles=false; g.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; g.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; foreach(DataGridViewColumn c in g.Columns){ c.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; c.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter; c.HeaderCell.Style.Font=g.ColumnHeadersDefaultCellStyle.Font; } if(!ReferenceEquals(g,_summary)) ApplyGridWidths(g); else ApplySummaryLayout();
        if(g.Columns.Contains("浮动收益")||g.Columns.Contains("盈亏")||g.Columns.Contains("盈亏金额")){ g.CellFormatting-=Grid_CellFormatting; g.CellFormatting+=Grid_CellFormatting; } try{ g.ClearSelection(); g.CurrentCell=null; }catch{}
    }
    void ApplySummaryLayout(){
        try{
            _applyingLayout=true;
            _summary.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
            _summary.EnableHeadersVisualStyles=false;
            foreach(DataGridViewColumn c in _summary.Columns){
                c.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Font=_summary.ColumnHeadersDefaultCellStyle.Font;
            }
            var ui=_store.LoadUi();
            if(ui.GridWidths.TryGetValue(_summary.Name,out var map) && map.Count>0){
                foreach(DataGridViewColumn c in _summary.Columns){ if(map.TryGetValue(c.Name,out var w)) try{ c.Width=Math.Max(70,Math.Min(1200,w)); }catch{} }
            }else{
                var total=Math.Max(900,_summary.ClientSize.Width-24);
                if(_summary.Columns.Contains("项目")) _summary.Columns["项目"].Width=Math.Max(220,(int)(total*0.22));
                if(_summary.Columns.Contains("数值")) _summary.Columns["数值"].Width=Math.Max(340,(int)(total*0.30));
                if(_summary.Columns.Contains("说明")) _summary.Columns["说明"].Width=Math.Max(460,total-(_summary.Columns.Contains("项目")?_summary.Columns["项目"].Width:0)-(_summary.Columns.Contains("数值")?_summary.Columns["数值"].Width:0));
            }
            _summary.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            _summary.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
            foreach(DataGridViewRow r in _summary.Rows) if(r.Height<42) r.Height=42;
        }catch{} finally{ _applyingLayout=false; }
    }
    void ApplyGridWidths(DataGridView g){
        try{
            if(g.Columns.Count==0 || g.IsDisposed) return;
            _applyingLayout=true;
            g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
            g.EnableHeadersVisualStyles=false;
            var ui=_store.LoadUi();
            Dictionary<string,int>? map=null;
            var hasMap=!string.IsNullOrWhiteSpace(g.Name)&&ui.GridWidths.TryGetValue(g.Name,out map)&&map.Count>0;
            foreach(DataGridViewColumn c in g.Columns){
                if(c==null || string.IsNullOrWhiteSpace(c.Name)) continue;
                c.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter;
                c.HeaderCell.Style.Font=g.ColumnHeadersDefaultCellStyle.Font;
                if(hasMap && map!=null && map.TryGetValue(c.Name,out var w)){
                    try{ c.Width=Math.Max(45,Math.Min(1200,w)); }catch{}
                }else{
                    var basis=TextRenderer.MeasureText(c.HeaderText??c.Name, g.ColumnHeadersDefaultCellStyle.Font??g.Font).Width+30;
                    try{ c.Width=Math.Max(56,Math.Min(260,basis)); }catch{}
                }
            }
        }catch{} finally{ _applyingLayout=false; }
    }
    void Grid_DataError(object? sender, DataGridViewDataErrorEventArgs e){ e.ThrowException=false; e.Cancel=true; }
    void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e){ var g=sender as DataGridView; if(g==null||e.RowIndex<0||e.ColumnIndex<0)return; var col=g.Columns[e.ColumnIndex].Name; if((col=="浮动收益"||col=="盈亏金额") && e.Value!=null && decimal.TryParse(Convert.ToString(e.Value),out var v)){ e.Value=v.ToString("0.########",CultureInfo.InvariantCulture); e.FormattingApplied=true; e.CellStyle.ForeColor=v>=0?Color.Green:Color.Red; e.CellStyle.Font=new Font(g.Font,FontStyle.Bold); e.CellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; } else if(col=="盈亏" && e.Value!=null){ var txt=Convert.ToString(e.Value); e.CellStyle.ForeColor=txt=="亏"?Color.Red:Color.Green; e.CellStyle.Font=new Font(g.Font,FontStyle.Bold); e.CellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; } }
    void LoadTree(){ _tree.BeginUpdate(); _tree.Nodes.Clear(); var mains=_store.Query("SELECT * FROM main_positions WHERE account_key=$a AND status<>'已平仓' ORDER BY id",("$a",AccountKey)); foreach(DataRow m in mains.Rows){ var n=new TreeNode($"{m["code"]} 主仓｜{m["direction"]}｜{m["status"]}"){Tag=new TagInfo("main",Convert.ToInt64(m["id"])),ImageKey=StatusKey(Convert.ToString(m["status"])),SelectedImageKey=StatusKey(Convert.ToString(m["status"]))}; _tree.Nodes.Add(n); var nodes=_store.Query("SELECT * FROM position_nodes WHERE account_key=$a AND main_code=$m AND node_type IN ('加仓','对冲') AND status<>'已平仓' ORDER BY id",("$a",AccountKey),("$m",Convert.ToString(m["code"])!)); foreach(DataRow r in nodes.Rows){ n.Nodes.Add(new TreeNode($"{r["code"]} {r["node_type"]}｜{r["direction"]}｜{r["status"]}｜{Fmt(r["qty"])}"){Tag=new TagInfo("node",Convert.ToInt64(r["id"])),ImageKey=StatusKey(Convert.ToString(r["status"])),SelectedImageKey=StatusKey(Convert.ToString(r["status"]))}); } n.Expand(); } _tree.EndUpdate(); }
    void LoadRisk(){ var root=_store.LoadLatest(out _); var s=FindSnap(root); var dt=new DataTable(); foreach(var c in new[]{"项目","结果","说明"})dt.Columns.Add(c); dt.Rows.Add("账户",_account.Text,"Binance/OKX独立主仓"); dt.Rows.Add("采集状态",s?.Status??"无数据",s?.LastSuccess??""); dt.Rows.Add("实时价格",FmtStr(s?.Price,1),"latest_snapshot"); dt.Rows.Add("持仓",Pos(s?.Position),"交易所真实持仓"); dt.Rows.Add("强平距离",LiqDistance(s),"标记价到强平价"); dt.Rows.Add("节点数量",_store.Scalar("SELECT COUNT(*) FROM position_nodes WHERE account_key=$a",("$a",AccountKey))?.ToString()??"0","A/H/TP/SL/R"); dt.Rows.Add("待归类订单",_store.Scalar("SELECT COUNT(*) FROM order_mappings WHERE account_key=$a AND match_status='待归类'",("$a",AccountKey))?.ToString()??"0","需要手动处理"); _riskGrid.DataSource=dt; HideId(_riskGrid); }
    void LoadScenario(){ _scenarioGrid.DataSource=_store.Query("SELECT id,target_price AS 目标价,created_at AS 时间,result_json AS 结果 FROM scenario_runs WHERE account_key=$a ORDER BY id DESC",("$a",AccountKey)); HideId(_scenarioGrid); }
    void BuildSummary(){ var root=_store.LoadLatest(out var err); var snap=FindSnap(root); var dt=new DataTable(); foreach(var c in new[]{"项目","数值","说明"})dt.Columns.Add(c); if(root==null){ dt.Rows.Add("采集器",err,"请先运行BTC实时通信系统"); _summary.DataSource=dt; return;} dt.Rows.Add("当前主仓",_account.Text,"Binance/OKX独立切换"); dt.Rows.Add("更新时间",root.updated_at??"--","latest_snapshot.json"); dt.Rows.Add("状态",snap?.Status??"--",snap?.LastSuccess??""); dt.Rows.Add("实时价格",FmtStr(snap?.Price,1),"WebSocket/采集器"); dt.Rows.Add("标记价",FmtStr(snap?.Mark,1),"用于风险参考"); dt.Rows.Add("强平价",FmtStr(snap?.Liq,1),"交易所返回"); dt.Rows.Add("持仓",Pos(snap?.Position),"交易所真实持仓"); dt.Rows.Add("浮动收益",FmtStr(snap?.Upnl,4),"浮盈绿色/浮亏红色在表格显示"); dt.Rows.Add("强平距离",LiqDistance(snap),"标记价到强平价"); _summary.DataSource=dt; HideId(_summary); _status.Text=$"{_account.Text} | {DateTime.Now:HH:mm:ss}"; }
    CollectorSnapshot? FindSnap(SnapshotRoot? root)=>root==null?null:(AccountKey=="BINANCE_UM"?root.snapshots.FirstOrDefault(x=>x.Exchange.StartsWith("Binance")):root.snapshots.FirstOrDefault(x=>x.Exchange.StartsWith("OKX")));

    static string StatusBall(string? s)=>(s=="持有中"||s=="持仓中")?"🔴":s=="成本止损"?"🟡":s=="移动止盈"?"🟢":"⚪";
    long? CurrentNodeId(){ if(_tabs.SelectedTab?.Text=="加仓A") return RowId(_addGrid); if(_tabs.SelectedTab?.Text=="对冲H") return RowId(_hedgeGrid); if(_tabs.SelectedTab?.Text=="减仓R") return RowId(_reduceGrid); if(_tabs.SelectedTab?.Text=="已平仓") return RowId(_closedGrid); return RowId(_nodeGrid); }
    void ReduceMain(){ var id=RowId(_mainGrid); if(id==null)return; var dt=_store.Query("SELECT * FROM main_positions WHERE id=$id",("$id",id)); if(dt.Rows.Count==0)return; var r=dt.Rows[0]; using var f=new ReduceEditForm(r,_store.Config.DefaultFeeRatePct,NextNodeCode("减仓"),"main"); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; var dir=Convert.ToString(r["direction"])??"多"; var fee=CalcNodeFee(AccountKey,v.Qty,v.Close,_store.Config.DefaultFeeRatePct); var pnl=CalcPnl(AccountKey,dir,v.Qty,v.Entry,v.Close)-fee; _store.Exec("INSERT INTO position_nodes(account_key,main_code,code,node_type,direction,status,qty,entry_price,close_exit_price,fee,floating_pnl,reduce_source_code,exchange_open_order_id,exchange_close_order_id,created_at,close_time,note) VALUES($a,$m,$c,'减仓',$d,'已减仓',$q,$e,$cl,$f,$pnl,$src,$oo,$co,$ct,$tm,$n)",("$a",AccountKey),("$m",v.MainCode),("$c",v.Code),("$d",dir),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",fee),("$pnl",pnl),("$src",v.SourceCode),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$ct",v.OpenTime),("$n",v.Note)); RefreshKeepWidths(); }}
    void ReduceCurrentNode(){ var id=CurrentNodeId(); if(id==null)return; var dt=_store.Query("SELECT * FROM position_nodes WHERE id=$id",("$id",id)); if(dt.Rows.Count==0)return; var r=dt.Rows[0]; var st=Convert.ToString(r["status"]); if(st!="已开仓"&&st!="持有中"&&st!="成本止损"&&st!="移动止盈"){ MessageBox.Show(this,"只有当前持有中的订单才能减仓"); return;} using var f=new ReduceEditForm(r, _store.Config.DefaultFeeRatePct, NextNodeCode("减仓")); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; var dir=Convert.ToString(r["direction"])??"多"; var fee=v.Fee==0?CalcNodeFee(AccountKey,v.Qty,v.Close,_store.Config.DefaultFeeRatePct):v.Fee; var pnl=CalcPnl(AccountKey,dir,v.Qty,v.Entry,v.Close)-fee; _store.Exec("INSERT INTO position_nodes(account_key,main_code,code,node_type,direction,status,qty,entry_price,close_exit_price,fee,floating_pnl,reduce_source_code,exchange_open_order_id,exchange_close_order_id,created_at,close_time,note) VALUES($a,$m,$c,'减仓',$d,'已减仓',$q,$e,$cl,$f,$pnl,$src,$oo,$co,$ct,$tm,$n)",("$a",AccountKey),("$m",v.MainCode),("$c",v.Code),("$d",dir),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",fee),("$pnl",pnl),("$src",v.SourceCode),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$ct",v.OpenTime),("$n",v.Note)); RefreshKeepWidths(); }}
    void EditCurrentOrClosed(){ if(_tabs.SelectedTab?.Text=="已平仓") { EditClosed(RowId(_closedGrid)); return; } EditNode(CurrentNodeId()); }
    void EditClosed(long? id){
        if(id==null)return;
        string Cell(string name){ try{ return Convert.ToString(_closedGrid.CurrentRow?.Cells[name].Value)??""; }catch{return "";} }
        var oldCode=Cell("编号"); var isNode=oldCode.Contains('-'); var rowType=Cell("类型"); if(string.IsNullOrWhiteSpace(rowType)) rowType=isNode?"节点":"主仓";
        using var f=new SimpleComboForm("修改已平仓",
            new FieldSpec("编号","text",Default:oldCode),
            new FieldSpec("方向","combo",new[]{"多","空"},Cell("方向")),
            new FieldSpec("状态","combo",new[]{"已平仓","持有中","成本止损","移动止盈","已取消","已完成"},Cell("状态")),
            new FieldSpec("类型","combo",isNode?new[]{"加仓","对冲","减仓"}:new[]{"主仓"},rowType),
            new FieldSpec("平仓类型","combo",new[]{"止损","成本止损","移动止盈","手动平仓"}, string.IsNullOrWhiteSpace(Cell("平仓类型"))?"手动平仓":Cell("平仓类型")),
            new FieldSpec("持仓量","text",Default:Cell("持仓量")),
            new FieldSpec("盈亏","combo",new[]{"盈","亏"}, string.IsNullOrWhiteSpace(Cell("盈亏"))?"盈":Cell("盈亏")),
            new FieldSpec("盈亏金额","text",Default:Cell("盈亏金额")),
            new FieldSpec("开仓均价","text",Default:Cell("开仓均价")),
            new FieldSpec("平仓价格","text",Default:Cell("平仓价格")),
            new FieldSpec("手续费","text",Default:Cell("手续费")),
            new FieldSpec("开仓时间","text",Default:Cell("开仓时间")),
            new FieldSpec("平仓时间","text",Default:Cell("平仓时间")),
            new FieldSpec("开仓订单号","text",Default:Cell("开仓订单号")),
            new FieldSpec("平仓订单号","text",Default:Cell("平仓订单号")),
            new FieldSpec("备注","text",Default:Cell("备注")));
        if(f.ShowDialog(this)==DialogResult.OK){
            var v=f.Values; decimal pnl=Dec(v[7]); if(v[6]=="亏" && pnl>0) pnl=-pnl; if(v[6]=="盈" && pnl<0) pnl=Math.Abs(pnl);
            if(isNode){
                var full=v[0]; var mainCode=full.Contains('-')?full.Split('-')[0]:""; var nodeCode=full.Contains('-')?full.Split('-').Last():full;
                _store.Exec("UPDATE position_nodes SET main_code=CASE WHEN $m='' THEN main_code ELSE $m END,code=$c,direction=$d,status=$s,node_type=$typ,close_type=$cty,qty=$q,floating_pnl=$pnl,entry_price=$entry,close_exit_price=$close,fee=$fee,created_at=$ot,close_time=$ct,exchange_open_order_id=$oo,exchange_close_order_id=$co,note=$n WHERE id=$id",
                    ("$m",mainCode),("$c",nodeCode),("$d",v[1]),("$s",v[2]),("$typ",v[3]),("$cty",v[4]),("$q",DbNum(v[5])),("$pnl",pnl),("$entry",DbNum(v[8])),("$close",DbNum(v[9])),("$fee",DbNum(v[10])),("$ot",v[11]),("$ct",v[12]),("$oo",v[13]),("$co",v[14]),("$n",v[15]),("$id",id));
            }else{
                _store.Exec("UPDATE main_positions SET code=$c,direction=$d,status=$s,close_type=$cty,base_qty=$q,floating_pnl=$pnl,avg_price=$entry,close_price=$close,fee=$fee,created_at=$ot,close_time=$ct,exchange_open_order_id=$oo,exchange_close_order_id=$co,note=$n WHERE id=$id",
                    ("$c",v[0]),("$d",v[1]),("$s",v[2]),("$cty",v[4]),("$q",DbNum(v[5])),("$pnl",pnl),("$entry",DbNum(v[8])),("$close",DbNum(v[9])),("$fee",DbNum(v[10])),("$ot",v[11]),("$ct",v[12]),("$oo",v[13]),("$co",v[14]),("$n",v[15]),("$id",id));
            }
            RefreshKeepWidths();
        }
    }
    void SetProtection(string kind,long? forcedId=null){ var id=forcedId??CurrentNodeId(); if(id==null)return; var dt=_store.Query("SELECT status,node_type FROM position_nodes WHERE id=$id",("$id",id)); if(dt.Rows.Count==0)return; var status=Convert.ToString(dt.Rows[0]["status"]); var type=Convert.ToString(dt.Rows[0]["node_type"]); if(status!="持有中"&&status!="已开仓"&&status!="移动止盈"&&status!="成本止损"){ MessageBox.Show(this,"只有持有中的加仓/对冲节点才能添加"+kind); return;} if(type!="加仓"&&type!="对冲"){ MessageBox.Show(this,"只有加仓或对冲节点适用"+kind); return;} using var f=new SimpleComboForm("添加"+kind, new FieldSpec(kind+"价位","text")); if(f.ShowDialog(this)==DialogResult.OK){ var price=DbNum(f.Values[0]); if(kind=="成本止损") _store.Exec("UPDATE position_nodes SET cost_stop=$p,status='成本止损' WHERE id=$id",("$p",price),("$id",id)); else _store.Exec("UPDATE position_nodes SET trailing_take_profit=$p,status='移动止盈' WHERE id=$id",("$p",price),("$id",id)); RefreshKeepWidths(); }}
    void SetTreeProtection(string kind){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main") SetMainProtection(kind,t.Id); else SetProtection(kind,t.Id); }
    void CloseTreeSelected(){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main") CloseMain(t.Id); else CloseNode(t.Id); }
    void SetMainProtection(string kind,long? forcedId=null){ var id=forcedId??RowId(_mainGrid); if(id==null)return; using var f=new SimpleComboForm(kind,new FieldSpec(kind+"价位","text")); if(f.ShowDialog(this)==DialogResult.OK){ var price=DbNum(f.Values[0]); if(kind=="成本止损") _store.Exec("UPDATE main_positions SET cost_stop=$p,status='成本止损' WHERE id=$id",("$p",price),("$id",id)); else _store.Exec("UPDATE main_positions SET trailing_take_profit=$p,status='移动止盈' WHERE id=$id",("$p",price),("$id",id)); RefreshKeepWidths(); }}
    void CloseMain(long? forcedId=null){ var id=forcedId??RowId(_mainGrid); if(id==null)return; using var f=new SimpleComboForm("平仓",new FieldSpec("平仓价格","text"),new FieldSpec("平仓类型","combo",new[]{"止损","成本止损","移动止盈","手动平仓"},"手动平仓"),new FieldSpec("平仓时间","text",Default:""),new FieldSpec("平仓订单号","text")); if(f.ShowDialog(this)==DialogResult.OK){ _store.Exec("UPDATE main_positions SET close_price=$p,close_type=$cty,exchange_close_order_id=$o,close_time=$tm,status='已平仓',floating_pnl=(CASE WHEN direction='空' THEN (avg_price-$p)*base_qty ELSE ($p-avg_price)*base_qty END)-fee-ABS(base_qty*$p*0.0005) WHERE id=$id",("$p",DbNum(f.Values[0])),("$cty",f.Values[1]),("$tm",f.Values[2]),("$o",f.Values[3]),("$id",id)); RefreshKeepWidths(); }}
    void CloseNode(long? forcedId=null){ var id=forcedId??CurrentNodeId(); if(id==null)return; using var f=new SimpleComboForm("平仓",new FieldSpec("平仓价格","text"),new FieldSpec("平仓类型","combo",new[]{"止损","成本止损","移动止盈","手动平仓"},"手动平仓"),new FieldSpec("平仓时间","text",Default:""),new FieldSpec("平仓订单号","text")); if(f.ShowDialog(this)==DialogResult.OK){ _store.Exec("UPDATE position_nodes SET close_exit_price=$p,close_type=$cty,exchange_close_order_id=$o,close_time=$tm,status='已平仓',floating_pnl=(CASE WHEN direction='空' THEN (entry_price-$p)*qty ELSE ($p-entry_price)*qty END)-fee-ABS(qty*$p*0.0005) WHERE id=$id",("$p",DbNum(f.Values[0])),("$cty",f.Values[1]),("$tm",f.Values[2]),("$o",f.Values[3]),("$id",id)); RefreshKeepWidths(); }}
    string[] MainCodeOptions(long? id=null){ var used=_store.Query("SELECT code FROM main_positions WHERE account_key=$a",("$a",AccountKey)).Rows.Cast<DataRow>().Select(r=>Convert.ToString(r[0])??"").ToHashSet(); return Enumerable.Range(1,10).Select(i=>$"M{i}").OrderBy(c=>used.Contains(c)?1:0).ThenBy(c=>int.Parse(c[1..])).ToArray(); }
    string[] ActiveMainCodes(){ var arr=_store.Query("SELECT code FROM main_positions WHERE account_key=$a AND status<>'已平仓' ORDER BY id",("$a",AccountKey)).Rows.Cast<DataRow>().Select(r=>Convert.ToString(r[0])??"").Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(); return arr.Length>0?arr:new[]{"M1"}; }
    string NextNodeCode(string? t){ var used=_store.Query("SELECT code FROM position_nodes WHERE account_key=$a",("$a",AccountKey)).Rows.Cast<DataRow>().Select(r=>Convert.ToString(r[0])??"").ToHashSet(); foreach(var c in NodeCodesFor(t)) if(!used.Contains(c)) return c; return NodeCodesFor(t).First(); }
    static string[] NodeCodesFor(string? t){ string prefix=t switch{ "加仓"=>"A", "减仓"=>"R", "对冲"=>"H", "移动止盈"=>"TP", "成本止损"=>"SL", _=>"A"}; return Enumerable.Range(1,1000).Select(i=>$"{prefix}{i}").ToArray(); }
    void EditMain(long? id){ var row=id==null?null:_store.Query("SELECT * FROM main_positions WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault(); using var f=new MainEditForm(row, MainCodeOptions(id), AccountKey); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; var mainFee=v.Fee==0?CalcMainFee(AccountKey,v.Qty,v.Price,_store.Config.DefaultFeeRatePct):v.Fee; var mainBe=v.Breakeven==0?Breakeven(v.Direction,v.Price,_store.Config.DefaultFeeRatePct):v.Breakeven; if(id==null)_store.Exec("INSERT OR IGNORE INTO main_positions(account_key,code,direction,cycle_name,status,base_qty,avg_price,hard_stop,margin,fee,breakeven_price,exchange_open_order_id,created_at,note) VALUES($a,$c,$d,$cy,$s,$q,$p,$hs,$m,$f,$b,$oo,$ct,$n)",("$a",AccountKey),("$c",v.Code),("$d",v.Direction),("$cy",v.Cycle),("$s",v.Status),("$q",v.Qty),("$p",v.Price),("$hs",v.HardStop),("$m",v.Margin),("$f",mainFee),("$b",mainBe),("$oo",v.OpenOrder),("$ct",v.OpenTime),("$n",v.Note)); else _store.Exec("UPDATE main_positions SET code=$c,direction=$d,cycle_name=$cy,status=$s,base_qty=$q,avg_price=$p,hard_stop=$hs,margin=$m,fee=$f,breakeven_price=$b,exchange_open_order_id=$oo,created_at=$ct,note=$n WHERE id=$id",("$c",v.Code),("$d",v.Direction),("$cy",v.Cycle),("$s",v.Status),("$q",v.Qty),("$p",v.Price),("$hs",v.HardStop),("$m",v.Margin),("$f",mainFee),("$b",mainBe),("$oo",v.OpenOrder),("$ct",v.OpenTime),("$n",v.Note),("$id",id)); RefreshKeepWidths(); } }
    void EditNodePreset(string nodeType){ EditNode(null, nodeType); }
    void EditNode(long? id){ EditNode(id, null); }
    void EditNode(long? id, string? presetType){
        var row=id==null?null:_store.Query("SELECT * FROM position_nodes WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault();
        var typeForCodes=presetType ?? (row==null?"加仓":Convert.ToString(row["node_type"]));
        var mains=ActiveMainCodes(); using var f=new NodeEditForm(row,_store.Config.DefaultFeeRatePct,presetType, NodeCodesFor(typeForCodes), id==null?NextNodeCode(typeForCodes):Convert.ToString(row?["code"]), AccountKey, mains, mains.FirstOrDefault()??"M1");
        if(f.ShowDialog(this)==DialogResult.OK){
            var v=f.V; var autoFee=v.Fee==0?CalcNodeFee(AccountKey,v.Qty,v.Entry,_store.Config.DefaultFeeRatePct):v.Fee; var autoBe=v.Breakeven==0?Breakeven(v.Direction,v.Entry,_store.Config.DefaultFeeRatePct):v.Breakeven;
            try{
                if(id==null)_store.Exec("INSERT INTO position_nodes(account_key,main_code,code,node_type,direction,status,qty,entry_price,stop_loss,fee,funding,breakeven_price,cost_stop,trailing_take_profit,exchange_open_order_id,exchange_close_order_id,created_at,note) VALUES($a,$m,$c,$t,$d,$s,$q,$e,$cl,$f,$fu,$b,$cs,$tp,$oo,$co,$ct,$n)",("$a",AccountKey),("$m",v.MainCode),("$c",v.Code),("$t",v.Type),("$d",v.Direction),("$s",v.Status),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",autoFee),("$fu",v.Funding),("$b",autoBe),("$cs",v.CostStop),("$tp",v.TrailingTp),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$ct",v.OpenTime),("$n",v.Note));
                else _store.Exec("UPDATE position_nodes SET main_code=$m,code=$c,node_type=$t,direction=$d,status=$s,qty=$q,entry_price=$e,stop_loss=$cl,fee=$f,funding=$fu,breakeven_price=$b,cost_stop=$cs,trailing_take_profit=$tp,exchange_open_order_id=$oo,exchange_close_order_id=$co,created_at=$ct,note=$n WHERE id=$id",("$m",v.MainCode),("$c",v.Code),("$t",v.Type),("$d",v.Direction),("$s",v.Status),("$q",v.Qty),("$e",v.Entry),("$cl",v.Close),("$f",autoFee),("$fu",v.Funding),("$b",autoBe),("$cs",v.CostStop),("$tp",v.TrailingTp),("$oo",v.OpenOrder),("$co",v.CloseOrder),("$ct",v.OpenTime),("$n",v.Note),("$id",id));
                RefreshKeepWidths();
            }catch(SqliteException ex) when(ex.SqliteErrorCode==19){ MessageBox.Show(this,"编号重复，请选择下一个未使用编号。","保存失败"); }
        }
    }
    void EditOrder(long? id){ var row=id==null?null:_store.Query("SELECT * FROM order_mappings WHERE id=$id",("$id",id)).Rows.Cast<DataRow>().FirstOrDefault(); using var f=new OrderEditForm(row); if(f.ShowDialog(this)==DialogResult.OK){ var v=f.V; if(id==null)_store.Exec("INSERT INTO order_mappings(account_key,order_id,trade_id,side,action,price,qty,fee,funding,ts,match_status,raw_note) VALUES($a,$o,$tr,$s,$ac,$p,$q,$f,$fu,$ts,$ms,$n)",("$a",AccountKey),("$o",v.OrderId),("$tr",v.TradeId),("$s",v.Side),("$ac",v.Action),("$p",v.Price),("$q",v.Qty),("$f",v.Fee),("$fu",v.Funding),("$ts",v.Ts),("$ms",v.Match),("$n",v.Note)); else _store.Exec("UPDATE order_mappings SET order_id=$o,trade_id=$tr,side=$s,action=$ac,price=$p,qty=$q,fee=$f,funding=$fu,ts=$ts,match_status=$ms,raw_note=$n WHERE id=$id",("$o",v.OrderId),("$tr",v.TradeId),("$s",v.Side),("$ac",v.Action),("$p",v.Price),("$q",v.Qty),("$f",v.Fee),("$fu",v.Funding),("$ts",v.Ts),("$ms",v.Match),("$n",v.Note),("$id",id)); RefreshKeepWidths(); } }
    void DeleteMain(){ var id=RowId(_mainGrid); if(id==null)return; if(MessageBox.Show(this,"删除主仓会同时删除该主仓节点，确定？","确认",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){ var code=Convert.ToString(_store.Scalar("SELECT code FROM main_positions WHERE id=$id",("$id",id)))??""; _store.Exec("DELETE FROM position_nodes WHERE account_key=$a AND main_code=$m",("$a",AccountKey),("$m",code)); _store.Exec("DELETE FROM main_positions WHERE id=$id",("$id",id)); RefreshKeepWidths(); } }
    void DeleteNode(){ var id=CurrentNodeId(); if(id==null)return; if(MessageBox.Show(this,"删除选中节点？","确认",MessageBoxButtons.YesNo)==DialogResult.Yes){ _store.Exec("DELETE FROM position_nodes WHERE id=$id",("$id",id)); RefreshKeepWidths(); } }
    void DeleteOrder(){ var id=RowId(_orderGrid); if(id==null)return; if(MessageBox.Show(this,"删除选中订单？","确认",MessageBoxButtons.YesNo)==DialogResult.Yes){ _store.Exec("DELETE FROM order_mappings WHERE id=$id",("$id",id)); RefreshKeepWidths(); } }
    void EditTreeSelected(){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main")EditMain(t.Id); else EditNode(t.Id); }
    void DeleteTreeSelected(){ if(_tree.SelectedNode?.Tag is not TagInfo t)return; if(t.Kind=="main"){ SelectGridRow(_mainGrid,t.Id); DeleteMain(); } else { SelectGridRow(_nodeGrid,t.Id); DeleteNode(); } }
    void SelectGridRow(DataGridView g,long id){ foreach(DataGridViewRow r in g.Rows) if(Convert.ToInt64(r.Cells["id"].Value)==id){ g.CurrentCell=r.Cells.Cast<DataGridViewCell>().First(c=>c.Visible); break; } }
    void RunScenario(){ using var f=new SimpleComboForm("情景模拟", new FieldSpec("目标BTC价格","text")); if(f.ShowDialog(this)==DialogResult.OK){ var target=Dec(f.Values[0]); var root=_store.LoadLatest(out _); var s=FindSnap(root); var mark=Dec(s?.Mark); var result=$"当前标记价 {mark:F1}，目标价 {target:F1}，价差 {target-mark:F1}。后续将按每个A/H分支逐项计算。"; _store.Exec("INSERT INTO scenario_runs(account_key,target_price,created_at,result_json) VALUES($a,$p,datetime('now','localtime'),$r)",("$a",AccountKey),("$p",target),("$r",result)); RefreshKeepWidths(); MessageBox.Show(this,result,"情景模拟"); } }
    void OpenSettings(){ using var f=new SettingsForm(_store.Config); if(f.ShowDialog(this)==DialogResult.OK){ _store.SaveConfig(f.Config); _timer.Interval=Math.Max(1,_store.Config.RefreshSeconds)*1000; RefreshKeepWidths(); } }
    static decimal CalcMainFee(string account, decimal qty, decimal price, decimal feePct)=> account=="OKX_COIN"? Math.Abs(qty*feePct/100m):Math.Abs(qty*price*feePct/100m); static decimal CalcNodeFee(string account, decimal qty, decimal price, decimal feePct)=> account=="OKX_COIN"? Math.Abs(qty*feePct/100m):Math.Abs(qty*price*feePct/100m); static decimal CalcPnl(string account,string dir,decimal qty,decimal entry,decimal close){ if(entry<=0||close<=0)return 0; var usdt=(dir=="空"?(entry-close):(close-entry))*qty; return account=="OKX_COIN"?usdt/close:usdt; } static decimal Breakeven(string dir, decimal entry, decimal feePct){ if(entry<=0)return 0; var fee=feePct*2/100m; return dir=="空"? entry*(1-fee): entry*(1+fee); } static decimal Dec(object? s)=>decimal.TryParse(Convert.ToString(s),NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; static object? DbNum(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:DBNull.Value; static string Fmt(object? o)=>Dec(o).ToString("F4"); static string FmtStr(string? s,int n)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d.ToString("F"+n):"--"; static string Pos(string? s)=>string.IsNullOrWhiteSpace(s)||s=="--"?"--":s.Replace("short ","空").Replace("long ","多"); static string LiqDistance(CollectorSnapshot? s){var m=Dec(s?.Mark);var l=Dec(s?.Liq);return m>0&&l>0?(Math.Abs(l-m)/m*100m).ToString("F2")+"%":"--";} record TagInfo(string Kind,long Id);
}

public record FieldSpec(string Label,string Kind,string[]? Options=null,string Default="");
public class SimpleComboForm:Form
{
    protected readonly List<Control> _inputs=new(); public string[] Values=>_inputs.Select(c=>c is ComboBox cb?Convert.ToString(cb.SelectedItem)??cb.Text:c.Text).ToArray();
    public SimpleComboForm(string title, params FieldSpec[] fields){ Text=title; StartPosition=FormStartPosition.CenterParent; MinimizeBox=false; MaximizeBox=false; FormBorderStyle=FormBorderStyle.FixedDialog; Font=new Font("Microsoft YaHei",9); Width=720; Height=Math.Min(760,120+fields.Length*42); var p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=2,RowCount=fields.Length+1}; p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(p); int r=0; foreach(var f in fields){ p.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); p.Controls.Add(new Label{Text=f.Label,Anchor=AnchorStyles.Left,AutoSize=true},0,r); Control input; if(f.Kind=="combo"||f.Options!=null){ var cb=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList}; cb.Items.AddRange(f.Options??Array.Empty<string>()); if(!string.IsNullOrWhiteSpace(f.Default)&&cb.Items.Contains(f.Default)) cb.SelectedItem=f.Default; else if(cb.Items.Count>0) cb.SelectedIndex=0; input=cb; } else input=new TextBox{Dock=DockStyle.Fill,Text=f.Default}; _inputs.Add(input); p.Controls.Add(input,1,r++); } var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,8,0,0)}; var save=new Button{Text="保存",Width=100,Height=32,DialogResult=DialogResult.OK}; var cancel=new Button{Text="取消",Width=100,Height=32,DialogResult=DialogResult.Cancel}; bottom.Controls.Add(cancel); bottom.Controls.Add(save); p.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); p.Controls.Add(bottom,0,r); p.SetColumnSpan(bottom,2); AcceptButton=save; CancelButton=cancel; }
}
public sealed class MainEditForm:SimpleComboForm{ public MainValue V=>new(Values[0],Values[1],Values[2],Values[3],Dec(Values[4]),Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Dec(Values[8]),Dec(Values[9]),Values[10],Values[11],Values[12]); public MainEditForm(DataRow? r,string[] codeOptions,string accountKey):base(r==null?"新增主仓":"修改主仓", new FieldSpec("编号","combo",codeOptions,S(r,"code",codeOptions.FirstOrDefault()??"M1")), new FieldSpec("方向","combo",new[]{"多","空"},S(r,"direction","多")), new FieldSpec("周期","combo",new[]{"牛","熊"},CycleShort(S(r,"cycle_name","牛"))), new FieldSpec("状态","combo",new[]{"持有中","计划中","已平仓","已完成","已取消"},S(r,"status","持有中")), new FieldSpec(accountKey=="OKX_COIN"?"主仓数量(BTC)":"主仓数量(USDT)","text",Default:S(r,"base_qty","")), new FieldSpec("主仓均价(USDT/USD)","text",Default:S(r,"avg_price","")), new FieldSpec("硬止损价(USDT/USD)","text",Default:S(r,"hard_stop","")), new FieldSpec(accountKey=="OKX_COIN"?"保证金(BTC)":"保证金(USDT)","text",Default:S(r,"margin","")), new FieldSpec(accountKey=="OKX_COIN"?"手续费(BTC自动)":"手续费(USDT自动)","text",Default:S(r,"fee","")), new FieldSpec("盈亏平衡价","text",Default:S(r,"breakeven_price","")), new FieldSpec("开仓时间","text",Default:S(r,"created_at","")), new FieldSpec("开仓订单号","text",Default:S(r,"exchange_open_order_id","")), new FieldSpec("备注","text",Default:S(r,"note",""))){
        void Recalc(){ if(_inputs.Count<10)return; if(decimal.TryParse(_inputs[4].Text,NumberStyles.Any,CultureInfo.InvariantCulture,out var q)&&decimal.TryParse(_inputs[5].Text,NumberStyles.Any,CultureInfo.InvariantCulture,out var e)&&e>0){ var fee=accountKey=="OKX_COIN"?Math.Abs(q*0.05m/100m):Math.Abs(q*e*0.05m/100m); _inputs[8].Text=fee.ToString("0.########",CultureInfo.InvariantCulture); var dir=(_inputs[1] as ComboBox)?.SelectedItem?.ToString()??_inputs[1].Text; var be=dir=="空"?e*(1-0.001m):e*(1+0.001m); _inputs[9].Text=be.ToString("0.########",CultureInfo.InvariantCulture); }}
        _inputs[4].TextChanged+=(_,_)=>Recalc(); _inputs[5].TextChanged+=(_,_)=>Recalc(); if(_inputs[1] is ComboBox cb) cb.SelectedIndexChanged+=(_,_)=>Recalc(); Recalc();
    } static string CycleShort(string s)=>s=="牛市周期"?"牛":s=="熊市周期"?"熊":s; static string[] CodesFor(string? t){ string prefix=t switch{ "加仓"=>"A", "减仓"=>"R", "对冲"=>"H", "移动止盈"=>"TP", "成本止损"=>"SL", _=>"A"}; return Enumerable.Range(1,1000).Select(i=>$"{prefix}{i}").ToArray(); } static string DefaultCode(string? t)=>CodesFor(t).First(); static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record MainValue(string Code,string Direction,string Cycle,string Status,decimal Qty,decimal Price,decimal HardStop,decimal Margin,decimal Fee,decimal Breakeven,string OpenTime,string OpenOrder,string Note);
public sealed class NodeEditForm:SimpleComboForm{ public NodeValue V=>new(Values[0],Values[1],Values[2],Values[3],Values[4],Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Dec(Values[8]),0,Dec(Values[9]),0,0,Values[11],"",Values[10],Values[12]); public NodeEditForm(DataRow? r,decimal feeRate,string? presetType=null,string[]? codeOptions=null,string? defaultCode=null,string accountKey="OKX_COIN",string[]? mainOptions=null,string? defaultMain=null):base(r==null?($"新增{presetType ?? "节点"}"):"修改节点", new FieldSpec("主仓编号","combo",mainOptions??Enumerable.Range(1,10).Select(i=>$"M{i}").ToArray(),S(r,"main_code",defaultMain??"M1")), new FieldSpec("节点编号","combo",codeOptions??CodesFor(presetType),S(r,"code",defaultCode??DefaultCode(presetType))), new FieldSpec("类型","combo",new[]{"加仓","对冲","减仓"},S(r,"node_type",presetType ?? "加仓")), new FieldSpec("方向","combo",new[]{"多","空"},S(r,"direction","多")), new FieldSpec("状态","combo",new[]{"持有中","计划中","成本止损","移动止盈","已平仓","已取消","待归类"},S(r,"status","持有中")), new FieldSpec(accountKey=="OKX_COIN"?"数量(BTC)":"数量(USDT)","text",Default:S(r,"qty","")), new FieldSpec("开仓价","text",Default:S(r,"entry_price","")), new FieldSpec("止损价","text",Default:S(r,"close_price","")), new FieldSpec("手续费(自动)","text",Default:S(r,"fee","")), new FieldSpec("盈亏平衡(自动)","text",Default:S(r,"breakeven_price","")), new FieldSpec("开仓时间","text",Default:S(r,"created_at","")), new FieldSpec("开仓订单号","text",Default:S(r,"exchange_open_order_id","")), new FieldSpec("备注","text",Default:S(r,"note",""))){
        void Recalc(){
            if(_inputs.Count<10) return;
            var qtyTxt=_inputs[5].Text; var entryTxt=_inputs[6].Text;
            if(decimal.TryParse(qtyTxt,NumberStyles.Any,CultureInfo.InvariantCulture,out var q) && decimal.TryParse(entryTxt,NumberStyles.Any,CultureInfo.InvariantCulture,out var e) && e>0){
                _inputs[8].Text=(Math.Abs(q*e*feeRate/100m)).ToString("0.########",CultureInfo.InvariantCulture);
                var dir=(_inputs[3] as ComboBox)?.SelectedItem?.ToString() ?? _inputs[3].Text;
                var be=dir=="空"? e*(1-feeRate*2/100m): e*(1+feeRate*2/100m);
                _inputs[9].Text=be.ToString("0.########",CultureInfo.InvariantCulture);
            }
        }
        _inputs[5].TextChanged+=(_,_)=>Recalc(); _inputs[6].TextChanged+=(_,_)=>Recalc();
        if(_inputs[3] is ComboBox cb) cb.SelectedIndexChanged+=(_,_)=>Recalc();
        Recalc();
    } static string CycleShort(string s)=>s=="牛市周期"?"牛":s=="熊市周期"?"熊":s; static string[] CodesFor(string? t){ string prefix=t switch{ "加仓"=>"A", "减仓"=>"R", "对冲"=>"H", "移动止盈"=>"TP", "成本止损"=>"SL", _=>"A"}; return Enumerable.Range(1,1000).Select(i=>$"{prefix}{i}").ToArray(); } static string DefaultCode(string? t)=>CodesFor(t).First(); static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record NodeValue(string MainCode,string Code,string Type,string Direction,string Status,decimal Qty,decimal Entry,decimal Close,decimal Fee,decimal Funding,decimal Breakeven,decimal CostStop,decimal TrailingTp,string OpenOrder,string CloseOrder,string OpenTime,string Note);
public sealed class ReduceEditForm:SimpleComboForm{ public ReduceValue V=>new(Values[0],Values[1],Values[2],Values[3],Dec(Values[4]),Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Values[8],Values[9],Values[10],Values[11],Values[12]); public ReduceEditForm(DataRow r,decimal feeRate,string code,string sourceKind="node"):base("减仓", new FieldSpec("主仓编号","text",Default:S(r,sourceKind=="main"?"code":"main_code","")), new FieldSpec("编号","combo",Enumerable.Range(1,1000).Select(i=>$"R{i}").ToArray(),code), new FieldSpec("减仓订单编号","text",Default:S(r,"code","")), new FieldSpec("状态","combo",new[]{"已减仓"},"已减仓"), new FieldSpec("减仓量","text",Default:""), new FieldSpec("开仓均价","text",Default:S(r,sourceKind=="main"?"avg_price":"entry_price","")), new FieldSpec("减仓价格","text",Default:""), new FieldSpec("手续费","text",Default:""), new FieldSpec("开仓订单号","text",Default:S(r,"exchange_open_order_id","")), new FieldSpec("开仓时间","text",Default:S(r,"created_at","")), new FieldSpec("平仓时间","text",Default:""), new FieldSpec("平仓订单号","text",Default:""), new FieldSpec("备注","text",Default:"")){ void Recalc(){ if(decimal.TryParse(_inputs[4].Text,NumberStyles.Any,CultureInfo.InvariantCulture,out var q)&&decimal.TryParse(_inputs[6].Text,NumberStyles.Any,CultureInfo.InvariantCulture,out var px)){ _inputs[7].Text=(Math.Abs(q*px*feeRate/100m)).ToString("0.########",CultureInfo.InvariantCulture);} } _inputs[4].TextChanged+=(_,_)=>Recalc(); _inputs[6].TextChanged+=(_,_)=>Recalc(); } static string S(DataRow? r,string c,string d)=>r==null||!r.Table.Columns.Contains(c)?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record ReduceValue(string MainCode,string Code,string SourceCode,string Status,decimal Qty,decimal Entry,decimal Close,decimal Fee,string OpenOrder,string OpenTime,string CloseTime,string CloseOrder,string Note);
public sealed class OrderEditForm:SimpleComboForm{ public OrderValue V=>new(Values[0],Values[1],Values[2],Values[3],Dec(Values[4]),Dec(Values[5]),Dec(Values[6]),Dec(Values[7]),Values[8],Values[9],Values[10]); public OrderEditForm(DataRow? r):base(r==null?"新增订单":"修改订单", new FieldSpec("订单号","text",Default:S(r,"order_id","")), new FieldSpec("成交号","text",Default:S(r,"trade_id","")), new FieldSpec("方向","combo",new[]{"多","空","买入","卖出"},S(r,"side","多")), new FieldSpec("动作","combo",new[]{"开仓","平仓","加仓","对冲","止盈","止损","减仓","资金费"},S(r,"action","开仓")), new FieldSpec("价格","text",Default:S(r,"price","")), new FieldSpec("数量","text",Default:S(r,"qty","")), new FieldSpec("手续费","text",Default:S(r,"fee","")), new FieldSpec("资金费","text",Default:S(r,"funding","")), new FieldSpec("时间","text",Default:S(r,"ts",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))), new FieldSpec("匹配状态","combo",new[]{"已匹配","待归类","忽略"},S(r,"match_status","待归类")), new FieldSpec("备注","text",Default:S(r,"raw_note",""))){} static string CycleShort(string s)=>s=="牛市周期"?"牛":s=="熊市周期"?"熊":s; static string[] CodesFor(string? t){ string prefix=t switch{ "加仓"=>"A", "减仓"=>"R", "对冲"=>"H", "移动止盈"=>"TP", "成本止损"=>"SL", _=>"A"}; return Enumerable.Range(1,1000).Select(i=>$"{prefix}{i}").ToArray(); } static string DefaultCode(string? t)=>CodesFor(t).First(); static string S(DataRow? r,string c,string d)=>r==null?d:Convert.ToString(r[c])??d; static decimal Dec(string s)=>decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)?d:0m; }
public record OrderValue(string OrderId,string TradeId,string Side,string Action,decimal Price,decimal Qty,decimal Fee,decimal Funding,string Ts,string Match,string Note);
public sealed class SettingsForm:SimpleComboForm{ public AppConfig Config{get;} public SettingsForm(AppConfig cfg):base("设置", new FieldSpec("采集器latest路径","text",Default:cfg.CollectorLatestPath), new FieldSpec("管理数据目录","text",Default:cfg.DataDir), new FieldSpec("刷新秒数","combo",new[]{"1","2","3","5","10","30","60"},cfg.RefreshSeconds.ToString()), new FieldSpec("默认单向手续费%","combo",new[]{"0.05","0.04","0.03","0.02","0.01"},cfg.DefaultFeeRatePct.ToString(CultureInfo.InvariantCulture))){ FormClosing+=(_,_)=>{}; Config=new AppConfig{CollectorLatestPath=Values.Length>0?Values[0]:cfg.CollectorLatestPath,DataDir=Values.Length>1?Values[1]:cfg.DataDir,RefreshSeconds=cfg.RefreshSeconds,DefaultFeeRatePct=cfg.DefaultFeeRatePct,MaxSingleRiskPct=cfg.MaxSingleRiskPct}; } protected override void OnFormClosing(FormClosingEventArgs e){ base.OnFormClosing(e); if(DialogResult==DialogResult.OK){ var vals=Values; Config.CollectorLatestPath=vals[0]; Config.DataDir=vals[1]; Config.RefreshSeconds=(int)(decimal.TryParse(vals[2],out var s)?s:3); Config.DefaultFeeRatePct=decimal.TryParse(vals[3],NumberStyles.Any,CultureInfo.InvariantCulture,out var f)?f:0.05m; } } }
