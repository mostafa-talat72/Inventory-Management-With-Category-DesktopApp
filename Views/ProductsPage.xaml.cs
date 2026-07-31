using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ProductApp.Converters;
using ProductApp.Data;
using ProductApp.Models;
using ProductApp.Services;

namespace ProductApp.Views;

public partial class ProductsPage : Page
{
    private readonly AppDbContext _db;
    private readonly DispatcherTimer _searchTimer = new();
    private string? _currentSearch;
    private bool _loaded;
    private bool _isLoading;
    private bool _lowStockOnly;
    private List<Product>? _allProducts;
    private Dictionary<int, (int Total, decimal Value)>? _stockDataDict;
    private decimal _totalStockValue;
    private int? _selectedCategoryId;

    private class CategoryCardItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SubText { get; set; } = "";
        public string Count { get; set; } = "";
        public ICommand SelectCommand { get; set; } = null!;
        public ICommand EditCommand { get; set; } = null!;
        public ICommand DeleteCommand { get; set; } = null!;
        public ICommand ContextCommand { get; set; } = null!;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private ObservableCollection<CategoryCardItem> _categoryCards = new();
    private List<CategoryCardItem> _allCategoryCards = new();

    public ProductsPage()
    {
        _db = new AppDbContext();
        InitializeComponent();

        CategoryCardsList.ItemsSource = _categoryCards;

        Loaded += (_, _) =>
        {
            AmountsVisibilityService.VisibilityChanged += OnAmountsVisibilityChanged;
            LoadCategories();
        };
        Unloaded += (_, _) =>
        {
            AmountsVisibilityService.VisibilityChanged -= OnAmountsVisibilityChanged;
            _db.Dispose();
        };

        _searchTimer.Interval = TimeSpan.FromMilliseconds(300);
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadProducts();
        };

        _loaded = true;
        LoadProducts();
    }

    private void OnAmountsVisibilityChanged()
    {
        ApplyAmountsMask();
    }

    private void ApplyAmountsMask()
    {
        const string mask = "••••••";
        bool hidden = AmountsVisibilityService.IsHidden;

        TxtStockValue.Text = hidden ? mask : $"{_totalStockValue:0.##} ج.م";

        if (_allProducts == null || _stockDataDict == null) return;
        BuildProductCards();
    }

    private void LoadCategories()
    {
        var allCats = _db.Categories.OrderBy(c => c.Name).ToList();
        TxtCategoryCount.Text = $"{allCats.Count} قسم";

        var productCounts = _db.Products
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        _categoryCards.Clear();

        var rootCats = allCats.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToList();
        foreach (var cat in rootCats)
            AddCategoryCards(cat, allCats, productCounts, 0);

        if (_selectedCategoryId != null)
        {
            var card = _categoryCards.FirstOrDefault(c => c.Id == _selectedCategoryId);
            if (card != null) card.IsSelected = true;
        }

        // حفظ نسخة كاملة للفلترة
        _allCategoryCards = _categoryCards.ToList();

        // تطبيق نص البحث الحالي لو موجود
        var searchText = TxtCategorySearch?.Text?.Trim();
        if (!string.IsNullOrEmpty(searchText))
            FilterCategoryCards(searchText);

        ApplyAmountsMask();
    }

    private void AddCategoryCards(Category cat, List<Category> allCats,
        Dictionary<int, int> counts, int depth)
    {
        int count = counts.GetValueOrDefault(cat.Id, 0);
        int subCatCount = allCats.Count(c => c.ParentCategoryId == cat.Id);
        var subText = subCatCount > 0 ? $"{subCatCount} قسم فرعي" : "";
        var catCopy = cat;

        var card = new CategoryCardItem
        {
            Id = cat.Id,
            Name = (depth > 0 ? new string(' ', depth * 3) + "↳ " : "") + cat.Name,
            SubText = subText,
            Count = count > 0 ? $"{count}" : "",
            IsSelected = _selectedCategoryId == cat.Id
        };
        card.SelectCommand = new RelayCommand(() => SelectCategory(card));
        card.EditCommand   = new RelayCommand(() => OpenEditCategory(catCopy));
        card.DeleteCommand = new RelayCommand(() => DeleteCategory(catCopy));
        card.ContextCommand = new RelayCommand(() => { });

        _categoryCards.Add(card);

        foreach (var child in allCats.Where(c => c.ParentCategoryId == cat.Id).OrderBy(c => c.Name))
            AddCategoryCards(child, allCats, counts, depth + 1);
    }

    private void SelectCategory(CategoryCardItem selected)
    {
        foreach (var c in _categoryCards) c.IsSelected = false;

        if (_selectedCategoryId == selected.Id)
        {
            _selectedCategoryId = null;
            SetAllProductsCardState(true);
        }
        else
        {
            selected.IsSelected = true;
            _selectedCategoryId = selected.Id;
            SetAllProductsCardState(false);
        }
        LoadProducts();
    }

    private void AllProducts_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedCategoryId = null;
        foreach (var c in _categoryCards) c.IsSelected = false;
        SetAllProductsCardState(true);
        LoadProducts();
    }

    private void SetAllProductsCardState(bool selected)
    {
        if (selected)
        {
            AllProductsCard.Background = (Brush)Application.Current.FindResource("PrimaryBrush");
            AllProductsCard.BorderThickness = new Thickness(0);
            AllProductsText.Foreground = Brushes.White;
            AllProductsIcon.Fill = Brushes.White;
            AllProductsIconBadge.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            AllProductsCard.Background = (Brush)Application.Current.FindResource("CardBackground");
            AllProductsCard.BorderThickness = new Thickness(1);
            AllProductsText.Foreground = (Brush)Application.Current.FindResource("HeadingTextBrush");
            AllProductsIcon.Fill = (Brush)Application.Current.FindResource("BodyTextBrush");
            AllProductsIconBadge.Background = (Brush)Application.Current.FindResource("SurfaceBackground");
        }
    }

    private void TxtCategorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCategoryCards(TxtCategorySearch.Text.Trim());
    }

    private void TglLowStockOnly_Changed(object sender, RoutedEventArgs e)
    {
        _lowStockOnly = TglLowStockOnly.IsChecked == true;
        LoadProducts();
    }

    private void FilterCategoryCards(string filter)
    {
        _categoryCards.Clear();
        var source = string.IsNullOrEmpty(filter)
            ? _allCategoryCards
            : _allCategoryCards.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var c in source)
            _categoryCards.Add(c);
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new CategoryDialog(_db);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadCategories();
        };
    }

    private void OpenEditCategory(Category cat)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new CategoryDialog(_db, cat);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadCategories();
        };
    }

    private void DeleteCategory(Category cat)
    {
        ConfirmDialog.Show("تأكيد الحذف", $"هل أنت متأكد من حذف القسم {cat.Name}؟", result =>
        {
            if (!result) return;
            var tracked = _db.Categories.Find(cat.Id);
            if (tracked == null) return;

            var subCats = _db.Categories.Where(c => c.ParentCategoryId == cat.Id).ToList();
            foreach (var sub in subCats)
                sub.ParentCategoryId = null;

            var products = _db.Products.Where(p => p.CategoryId == cat.Id).ToList();
            foreach (var p in products)
                p.CategoryId = null;

            _db.Categories.Remove(tracked);
            _db.SaveChanges();

            if (_selectedCategoryId == cat.Id)
                _selectedCategoryId = null;

            LoadCategories();
            LoadProducts();
        }, ConfirmDialog.DialogType.Danger);
    }

    private void LoadProducts()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            var query = _db.Products.AsNoTracking();

            if (_selectedCategoryId != null)
            {
                var catIds = GetCategoryAndDescendantIds(_selectedCategoryId.Value);
                query = query.Where(p => p.CategoryId != null && catIds.Contains(p.CategoryId.Value));
            }

            if (!string.IsNullOrWhiteSpace(_currentSearch))
                query = query.Where(p => p.Name.Contains(_currentSearch));

            _allProducts = query.Include(p => p.Units).OrderBy(p => p.Name).ToList();

            _stockDataDict = _db.InventoryBatches
                .GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(b => b.RemainingQuantity), Value = g.Sum(b => (decimal)b.RemainingQuantity * b.CostPricePerPiece) })
                .ToDictionary(x => x.ProductId, x => (Total: x.Total, Value: x.Value));

            if (_lowStockOnly)
                _allProducts = _allProducts
                    .Where(p => _stockDataDict.GetValueOrDefault(p.Id).Total <= 0)
                    .ToList();

            var inv = new InventoryService(_db);
            var totalStockPieces = 0;
            var lowStockCount = 0;
            _totalStockValue = 0m;

            foreach (var p in _allProducts)
            {
                var data = _stockDataDict.GetValueOrDefault(p.Id);
                totalStockPieces += data.Total;
                _totalStockValue  += data.Value;
                if (data.Total <= 0) lowStockCount++;
            }

            TxtTotalProducts.Text = _allProducts.Count.ToString();
            TxtTotalStock.Text    = totalStockPieces.ToString("0");
            TxtLowStock.Text      = lowStockCount.ToString();

            BuildProductCards();
            ApplyAmountsMask();
        }
        finally { _isLoading = false; }
    }

    private List<int> GetCategoryAndDescendantIds(int catId)
    {
        var ids = new List<int> { catId };
        var children = _db.Categories.Where(c => c.ParentCategoryId == catId).ToList();
        foreach (var child in children)
            ids.AddRange(GetCategoryAndDescendantIds(child.Id));
        return ids;
    }

    private void BuildProductCards()
    {
        const string mask = "••••••";
        bool hidden = AmountsVisibilityService.IsHidden;

        var inv = new InventoryService(_db);
        var cards = new List<object>();
        foreach (var p in _allProducts!)
        {
            var units = p.Units.OrderBy(u => u.UnitType).ToList();
            var data = _stockDataDict!.GetValueOrDefault(p.Id);
            var stockPieces = data.Total;
            var stockValue  = data.Value;
            var stockDisplay = inv.GetStockDisplay(p);

            var isLowStock = stockPieces <= 0;
            var (stockBg, stockFg) = isLowStock
                ? ("#FFEBEE", "#C62828")
                : ("#E8F5E9", "#2E7D32");

            cards.Add(new
            {
                p.Name,
                UnitsDisplay      = string.Join(" → ", units.Select(u => u.Name)),
                StockDisplay      = stockDisplay,
                StockBgColor      = stockBg,
                StockFgColor      = stockFg,
                StockValueDisplay = hidden ? mask : $"{stockValue:0.##} ج.م",
                RetailDisplay     = units.Count > 0 ? units.Min(u => u.RetailPrice).ToString("0.##")    : "-",
                WholesaleDisplay  = units.Count > 0 ? units.Min(u => u.WholesalePrice).ToString("0.##") : "-",
                Product           = p,
                SelectCommand     = new RelayCommand(() => OpenUnitLevelsDialog(p)),
                AddStockCommand   = new RelayCommand(() => OpenStockInForProduct(p)),
                DeductStockCommand = new RelayCommand(() => OpenStockDeductionForProduct(p)),
                HistoryCommand    = new RelayCommand(() => OpenStockMovementForProduct(p)),
                EditCommand       = new RelayCommand(() => OpenEditDialog(p)),
                DeleteCommand     = new RelayCommand(() => DeleteProduct(p))
            });
        }
        ProductsList.ItemsSource = cards;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        var text = SearchBox.Text;
        if (text == WatermarkBehavior.GetWatermark(SearchBox)) return;
        _currentSearch = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OpenUnitLevelsDialog(Product product)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new UnitLevelsDialog(_db, product);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadProducts();
        };
    }

    private void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        OpenProductDialog(null);
    }

    private void OpenEditDialog(Product product)
    {
        OpenProductDialog(product);
    }

    private void OpenProductDialog(Product? product)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new ProductDialog(_db, product);
        mainWindow.ShowOverlay(dialog);

        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true || r == null) LoadProducts();
        };

        dialog.ProductSwitchRequested += (s, targetProduct) =>
        {
            mainWindow.HideOverlay();
            LoadProducts();
            // افتح نافذة تعديل المنتج الجديد
            OpenProductDialog(targetProduct);
        };
    }

    private void DeleteProduct(Product product)
    {
        ConfirmDialog.Show("تأكيد الحذف", $"هل أنت متأكد من حذف {product.Name}؟", result =>
        {
            if (!result) return;
            _db.ProductUnits.RemoveRange(_db.ProductUnits.Where(u => u.ProductId == product.Id));
            _db.InventoryBatches.RemoveRange(_db.InventoryBatches.Where(b => b.ProductId == product.Id));
            _db.InventoryMovements.RemoveRange(_db.InventoryMovements.Where(m => m.ProductId == product.Id));
            var tracked = _db.Products.Find(product.Id);
            if (tracked != null) _db.Products.Remove(tracked);
            _db.SaveChanges();
            LoadProducts();
        }, ConfirmDialog.DialogType.Danger);
    }

    private void StockIn_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new StockInDialog();
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadProducts();
        };
    }

    private void OpenStockInForProduct(Product product)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new StockInDialog();
        dialog.PreSelectProduct(product);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadProducts();
        };
    }

    private void OpenStockDeductionForProduct(Product product)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new StockDeductionDialog(_db, product);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            if (r == true) LoadProducts();
        };
    }

    private void OpenStockMovementForProduct(Product product)
    {
        var mainWindow = (MainWindow)Window.GetWindow(this);
        var dialog = new StockMovementDialog(_db, product);
        mainWindow.ShowOverlay(dialog);
        dialog.DialogClosed += (s, r) =>
        {
            mainWindow.HideOverlay();
            LoadProducts();
        };
    }

    private void PrintInventory_Click(object sender, RoutedEventArgs e)
    {
        var inv = new InventoryService(_db);
        var printer = new ReceiptPrinter(_db);

        var batchValues = _db.InventoryBatches
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Value = g.Sum(b => (decimal)b.RemainingQuantity * b.CostPricePerPiece) })
            .ToDictionary(x => x.ProductId, x => x.Value);

        if (_selectedCategoryId != null)
        {
            // طباعة منتجات القسم المحدد فقط
            var catIds = GetCategoryAndDescendantIds(_selectedCategoryId.Value);
            var products = _db.Products
                .Include(p => p.Units)
                .Where(p => p.CategoryId != null && catIds.Contains(p.CategoryId.Value))
                .OrderBy(p => p.Name)
                .ToList();

            var printData = products.Select(p => (
                product: p,
                stockDisplay: inv.GetStockDisplay(p),
                totalPieces:  inv.GetAvailableStock(p),
                stockValue:   batchValues.GetValueOrDefault(p.Id, 0)
            )).ToList();

            var catName = _db.Categories.Find(_selectedCategoryId.Value)?.Name ?? "القسم";
            printer.PrintInventory(printData, catName);
        }
        else
        {
            // طباعة جميع المنتجات مقسّمة حسب الأقسام
            var allProducts = _db.Products
                .Include(p => p.Units)
                .OrderBy(p => p.CategoryId).ThenBy(p => p.Name)
                .ToList();

            var allCats = _db.Categories.OrderBy(c => c.Name).ToList();

            var printData = allProducts.Select(p => (
                product: p,
                stockDisplay: inv.GetStockDisplay(p),
                totalPieces:  inv.GetAvailableStock(p),
                stockValue:   batchValues.GetValueOrDefault(p.Id, 0)
            )).ToList();

            printer.PrintInventoryGrouped(printData, allCats);
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public RelayCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}