using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProductApp.Data;
using ProductApp.Models;
using ProductApp.Services;

namespace ProductApp.Views;

public partial class StockInDialog : UserControl
{
    public event EventHandler<bool?>? DialogClosed;

    private readonly AppDbContext _db;
    private readonly InventoryService _inv;
    private readonly ObservableCollection<StockInEntry> _selectedEntries = [];
    private List<Models.Product> _allProducts = [];
    private bool _loaded;
    private int? _selectedCategoryId = null;

    // ── Category chip model ──
    private class CategoryChip : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ICommand SelectCommand { get; set; } = null!;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly Dictionary<int, System.Windows.Threading.DispatcherTimer> _flashTimers = new();
    private readonly Dictionary<int, Brush?> _originalBrushes = new();
    private readonly ObservableCollection<CategoryChip> _chips = new();
    private List<CategoryChip> _allChips = new();

    public StockInDialog()
    {
        InitializeComponent();
        _db = new AppDbContext();
        _inv = new InventoryService(_db);
        SelectedItemsList.ItemsSource = _selectedEntries;
        CategoryChips.ItemsSource = _chips;
        LoadCategories();
        LoadProductCards();
        _loaded = true;
        Unloaded += (_, _) => { _db.Dispose(); };
    }

    private void LoadCategories()
    {
        var cats = _db.Categories.OrderBy(c => c.Name).ToList();
        _chips.Clear();
        foreach (var cat in cats)
        {
            var chip = new CategoryChip { Id = cat.Id, Name = cat.Name, IsSelected = _selectedCategoryId == cat.Id };
            chip.SelectCommand = new StockInRelayCommand(() =>
            {
                if (_selectedCategoryId == chip.Id)
                {
                    chip.IsSelected = false;
                    _selectedCategoryId = null;
                    SetChipAllActive(true);
                }
                else
                {
                    foreach (var c in _allChips) c.IsSelected = false;
                    chip.IsSelected = true;
                    _selectedCategoryId = chip.Id;
                    SetChipAllActive(false);
                }
                LoadProductCards();
            });
            _chips.Add(chip);
        }
        _allChips = _chips.ToList();

        var search = TxtCategorySearch?.Text?.Trim();
        if (!string.IsNullOrEmpty(search)) FilterChips(search);
    }

    private void SetChipAllActive(bool active)
    {
        if (ChipAll == null) return;
        ChipAll.Background = active
            ? (Brush)Application.Current.FindResource("PrimaryBrush")
            : (Brush)Application.Current.FindResource("CardBackground");
    }

    private void ChipAll_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedCategoryId = null;
        foreach (var c in _allChips) c.IsSelected = false;
        SetChipAllActive(true);
        LoadProductCards();
    }

    private void TxtCategorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterChips(TxtCategorySearch.Text.Trim());
    }

    private void FilterChips(string filter)
    {
        _chips.Clear();
        var source = string.IsNullOrEmpty(filter)
            ? _allChips
            : _allChips.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var c in source) _chips.Add(c);
    }

    private List<int> GetCategoryAndDescendantIds(int catId)
    {
        var ids = new List<int> { catId };
        var children = _db.Categories.Where(c => c.ParentCategoryId == catId).ToList();
        foreach (var child in children) ids.AddRange(GetCategoryAndDescendantIds(child.Id));
        return ids;
    }

    private void LoadProductCards(string? search = null)
    {
        var query = _db.Products.AsQueryable();

        if (_selectedCategoryId != null)
        {
            var catIds = GetCategoryAndDescendantIds(_selectedCategoryId.Value);
            query = query.Where(p => p.CategoryId != null && catIds.Contains(p.CategoryId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search));

        _allProducts = query.ToList();
        var cardItems = _allProducts.Select(p =>
        {
            var units = _db.ProductUnits.Where(u => u.ProductId == p.Id).OrderBy(u => u.UnitType).ToList();
            return new
            {
                p.Name,
                UnitsDisplay = string.Join(" → ", units.Select(u => u.Name)),
                StockDisplay = _inv.GetStockDisplay(p),
                SelectCommand = new StockInRelayCommand(() => AddProduct(p))
            };
        }).ToList();

        ProductCards.ItemsSource = cardItems;
    }

    public void PreSelectProduct(Models.Product product) => AddProduct(product);

    private void AddProduct(Models.Product product)
    {// لو موجود — اسكرول إليه وأضئه
        var existing = _selectedEntries.FirstOrDefault(e => e.ProductId == product.Id);
        if (existing != null)
        {
            ScrollToEntry(existing, highlight: true);
            return;
        }

        var units = _db.ProductUnits.Where(u => u.ProductId == product.Id).ToList();

        var entry = new StockInEntry
        {
            ProductId = product.Id,
            ProductName = product.Name,
            HasCarton = units.Any(u => u.UnitType == UnitType.Carton),
            HasBox    = units.Any(u => u.UnitType == UnitType.Box),
            HasPiece  = units.Any(u => u.UnitType == UnitType.Piece)
        };
        _selectedEntries.Add(entry);
        UpdateSelectedCount();

        // اسكرول للعنصر الجديد بعد render
        Dispatcher.InvokeAsync(() => ScrollToEntry(entry, highlight: false),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollToEntry(StockInEntry entry, bool highlight)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var container = SelectedItemsList.ItemContainerGenerator
                .ContainerFromItem(entry) as FrameworkElement;
            if (container == null) return;

            container.BringIntoView();

            if (!highlight) return;

            var border = FindFirstBorder(container);
            if (border == null) return;

            // أوقف timer سابق لنفس المنتج إن وجد
            if (_flashTimers.TryGetValue(entry.ProductId, out var old))
            {
                old.Stop();
                _flashTimers.Remove(entry.ProductId);
                // استعد اللون الأصلي
                if (_originalBrushes.TryGetValue(entry.ProductId, out var saved))
                {
                    border.Background = saved;
                    _originalBrushes.Remove(entry.ProductId);
                }
            }

            var original = border.Background;
            _originalBrushes[entry.ProductId] = original;

            int step = 0;
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(180) };
            _flashTimers[entry.ProductId] = timer;

            timer.Tick += (_, _) =>
            {
                step++;
                border.Background = step % 2 == 1
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x96))
                    : original;
                if (step >= 4)
                {
                    timer.Stop();
                    _flashTimers.Remove(entry.ProductId);
                    _originalBrushes.Remove(entry.ProductId);
                    border.Background = original;
                }
            };
            timer.Start();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static Border? FindFirstBorder(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b) return b;
            var found = FindFirstBorder(child);
            if (found != null) return found;
        }
        return null;
    }

    private void RemoveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is StockInEntry entry)
        {
            _selectedEntries.Remove(entry);
            UpdateSelectedCount();
        }
    }

    private void UpdateSelectedCount()
    {
        int count = _selectedEntries.Count;
        TxtSelectedBadge.Text = count.ToString();
        TxtSelectedCount.Text = count > 0
            ? $"({count} منتج محدد)"
            : "(لا توجد منتجات محددة)";
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        var text = TxtSearch.Text;
        if (text == ProductApp.Converters.WatermarkBehavior.GetWatermark(TxtSearch)) return;
        LoadProductCards(string.IsNullOrWhiteSpace(text) ? null : text.Trim());
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var toSave = _selectedEntries.Where(e => e.CartonQty > 0 || e.BoxQty > 0 || e.PieceQty > 0).ToList();
        if (toSave.Count == 0)
        {
            NotificationManager.ShowError("الرجاء اختيار منتجات وإدخال كميات");
            return;
        }

        foreach (var entry in _selectedEntries)
        {
            if (entry.TotalCost <= 0 && (entry.CartonQty > 0 || entry.BoxQty > 0 || entry.PieceQty > 0))
            {
                NotificationManager.ShowError($"الرجاء إدخال التكلفة الإجمالية لـ {entry.ProductName}");
                return;
            }
            if (!AreQuantitiesValid(entry))
            {
                NotificationManager.ShowError($"الرجاء إدخال أعداد صحيحة للكميات لـ {entry.ProductName}");
                return;
            }
        }

        foreach (var entry in toSave)
        {
            var product = _db.Products.Find(entry.ProductId);
            if (product != null)
            {
                await _inv.StockIn(product, entry.CartonQty, entry.BoxQty, entry.PieceQty, entry.TotalCost);
            }
        }

        App.AppBackup?.BackupIfOnOperation();

        NotificationManager.ShowSuccess("تم إضافة المخزون بنجاح");
        DialogClosed?.Invoke(this, true);
    }

    private static readonly HashSet<string> _qtyFields = ["CartonQty", "BoxQty", "PieceQty"];

    private bool AreQuantitiesValid(StockInEntry entry)
    {
        var container = SelectedItemsList.ItemContainerGenerator.ContainerFromItem(entry) as FrameworkElement;
        if (container == null) return true;
        var textBoxes = FindVisualChildren<TextBox>(container);
        foreach (var tb in textBoxes)
        {
            var expr = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            if (expr?.ResolvedSource != entry) continue;
            if (!_qtyFields.Contains(expr.ParentBinding.Path.Path)) continue;
            if (string.IsNullOrEmpty(tb.Text)) continue;
            if (!int.TryParse(tb.Text, out var val) || val < 0)
                return false;
        }
        return true;
    }

    private static List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var list = new List<T>();
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) list.Add(t);
            list.AddRange(FindVisualChildren<T>(child));
        }
        return list;
    }

    private void Qty_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogClosed?.Invoke(this, false);
    }
}

public class StockInEntry : INotifyPropertyChanged
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";

    private int _cartonQty;
    public int CartonQty { get => _cartonQty; set { _cartonQty = value; OnPropChanged(); } }

    private int _boxQty;
    public int BoxQty { get => _boxQty; set { _boxQty = value; OnPropChanged(); } }

    private int _pieceQty;
    public int PieceQty { get => _pieceQty; set { _pieceQty = value; OnPropChanged(); } }

    private decimal _totalCost;
    public decimal TotalCost { get => _totalCost; set { _totalCost = value; OnPropChanged(); } }

    public bool HasCarton { get; set; }
    public bool HasBox { get; set; }
    public bool HasPiece { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class StockInRelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
