using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using ProductApp.Data;
using ProductApp.Models;
using ProductApp.Services;

namespace ProductApp.Views;

public partial class ProductDialog : UserControl
{
    public event EventHandler<bool?>? DialogClosed;
    public event EventHandler<Product>? ProductSwitchRequested;

    private readonly AppDbContext _db;
    private readonly Product? _product;
    private readonly HashSet<string> _dirtyFields = [];
    private bool _loaded;
    private bool _isUpdating;
    private int? _selectedCategoryId = null;
    private List<Category> _allCategories = [];

    // ── Category dropdown ──
    private Border MakeCategoryChip(Category cat)
    {
        bool isSelected = cat.Id == 0 ? _selectedCategoryId == null : _selectedCategoryId == cat.Id;
        var row = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Tag = cat.Id
        };
        SetChipStyle(row, isSelected);
        var tb = new TextBlock { Text = cat.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        if (isSelected) { tb.FontWeight = FontWeights.SemiBold; tb.Foreground = Brushes.White; }
        else tb.SetResourceReference(TextBlock.ForegroundProperty, "HeadingTextBrush");
        row.Child = tb;
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selectedCategoryId = cat.Id == 0 ? null : (int?)cat.Id;
            RefreshChips();
            LoadCategoryProducts();
        };
        return row;
    }

    private void SetChipStyle(Border chip, bool selected)
    {
        if (selected)
        {
            chip.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            if (chip.Child is TextBlock tb) tb.Foreground = Brushes.White;
        }
        else
        {
            chip.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            if (chip.Child is TextBlock tb)
                tb.SetResourceReference(TextBlock.ForegroundProperty, "HeadingTextBrush");
        }
    }

    private void RefreshChips(string? filter = null)
    {
        CategoryChipsPanel.Children.Clear();
        CategoryChipsPanel.Children.Add(MakeCategoryChip(new Category { Id = 0, Name = "بدون قسم" }));
        var source = string.IsNullOrEmpty(filter)
            ? _allCategories
            : _allCategories.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var cat in source)
            CategoryChipsPanel.Children.Add(MakeCategoryChip(cat));
    }

    private void TxtCategorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshChips(TxtCategorySearch.Text.Trim());
    }

    private void LoadCategoryProducts()
    {
        if (_selectedCategoryId == null)
        {
            TxtCategoryProductsHeader.Text = "منتجات القسم";
            TxtCategoryProductsCount.Text = "اختر قسماً";
            CategoryProductsList.ItemsSource = null;
            return;
        }
        var products = _db.Products.AsNoTracking()
            .Where(p => p.CategoryId == _selectedCategoryId)
            .OrderBy(p => p.Name).ToList();
        TxtCategoryProductsHeader.Text = _allCategories.FirstOrDefault(c => c.Id == _selectedCategoryId)?.Name ?? "القسم";
        TxtCategoryProductsCount.Text = $"{products.Count} منتج";
        var items = products.Select(p =>
        {
            var units = _db.ProductUnits.AsNoTracking()
                .Where(u => u.ProductId == p.Id).OrderBy(u => u.UnitType).ToList();
            var pCopy = p;
            return new
            {
                p.Name,
                UnitsDisplay = string.Join(" ← ", units.Select(u => u.Name)),
                IsCurrentProduct = _product?.Id == p.Id,
                LoadCommand = new SimpleCommand(() => LoadProductForEdit(pCopy))
            };
        }).ToList();
        CategoryProductsList.ItemsSource = items;
    }

    private void LoadProductForEdit(Product product)
    {
        if (_product?.Id == product.Id) return;
        ProductSwitchRequested?.Invoke(this, product);
    }

    public ProductDialog(AppDbContext db, Product? product = null)
    {
        InitializeComponent();
        _db = db;
        _product = product;

        if (product != null)
        {
            LoadProductData();
            TxtHeader.Text = "تعديل المنتج";
        }
        else
        {
            ChkHasBox.IsChecked = true;
        }

        LoadCategories();
        _loaded = true;
        UpdateUnitLabels();

        // بعد التهيئة الكاملة نحمّل منتجات القسم
        Loaded += (_, _) => LoadCategoryProducts();
    }

    private void LoadCategories()
    {
        _allCategories = _db.Categories.OrderBy(c => c.Name).ToList();

        if (_product?.CategoryId != null)
            _selectedCategoryId = _product.CategoryId;

        RefreshChips();
    }

    private void LoadProductData()
    {
        _loaded = false;

        TxtName.Text = _product!.Name;
        TxtDescription.Text = _product.Description;
        BtnSave.Content = "حفظ التعديلات";

        var units = _db.ProductUnits.Where(u => u.ProductId == _product.Id).ToList();
        var piece = units.FirstOrDefault(u => u.UnitType == UnitType.Piece);
        var box = units.FirstOrDefault(u => u.UnitType == UnitType.Box);
        var carton = units.FirstOrDefault(u => u.UnitType == UnitType.Carton);

        ChkHasPiece.IsChecked = piece != null;
        if (piece != null)
        {
            TxtPieceName.Text = piece.Name;
            TxtPieceRetail.Text = piece.RetailPrice.ToString();
            TxtPieceWholesale.Text = piece.WholesalePrice == piece.RetailPrice ? "" : piece.WholesalePrice.ToString("0.##");
            TxtPieceMin.Text = piece.MinStockLevel > 0 ? piece.MinStockLevel.ToString() : "";
        }

        ChkHasBox.IsChecked = box != null;
        if (box != null)
        {
            TxtBoxName.Text = box.Name;
            TxtBoxQty.Text = box.QuantityPerParent.ToString();
            TxtBoxRetail.Text = box.RetailPrice.ToString("0.##");
            TxtBoxWholesale.Text = box.WholesalePrice == box.RetailPrice ? "" : box.WholesalePrice.ToString("0.##");
            TxtBoxMin.Text = box.MinStockLevel > 0 ? box.MinStockLevel.ToString() : "";
        }

        ChkHasCarton.IsChecked = carton != null;
        if (carton != null)
        {
            TxtCartonName.Text = carton.Name;
            TxtCartonQty.Text = carton.QuantityPerParent.ToString();
            TxtCartonRetail.Text = carton.RetailPrice.ToString("0.##");
            TxtCartonWholesale.Text = carton.WholesalePrice == carton.RetailPrice ? "" : carton.WholesalePrice.ToString("0.##");
            TxtCartonMin.Text = carton.MinStockLevel > 0 ? carton.MinStockLevel.ToString() : "";

            bool hasBox = units.Any(u => u.UnitType == UnitType.Box && u.ParentUnitId == carton.Id);
        }

        UpdateUnitLabels();
        _loaded = true;
    }

    private void ChkHasBox_Checked(object sender, RoutedEventArgs e)
    {
        BoxPanel.IsEnabled = true;
        BoxPanel.Opacity = 1;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void ChkHasBox_Unchecked(object sender, RoutedEventArgs e)
    {
        BoxPanel.IsEnabled = false;
        BoxPanel.Opacity = 0.5;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void ChkHasCarton_Checked(object sender, RoutedEventArgs e)
    {
        CartonPanel.IsEnabled = true;
        CartonPanel.Opacity = 1;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void ChkHasCarton_Unchecked(object sender, RoutedEventArgs e)
    {
        CartonPanel.IsEnabled = false;
        CartonPanel.Opacity = 0.5;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void ChkHasPiece_Checked(object sender, RoutedEventArgs e)
    {
        PiecePanel.IsEnabled = true;
        PiecePanel.Opacity = 1;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void ChkHasPiece_Unchecked(object sender, RoutedEventArgs e)
    {
        PiecePanel.IsEnabled = false;
        PiecePanel.Opacity = 0.5;
        _dirtyFields.Clear();
        UpdatePieceDependentFields();
        UpdateAutoPrices();
    }

    private void UpdatePieceDependentFields()
    {
        bool hasPiece = ChkHasPiece.IsChecked == true;
        bool hasBox = ChkHasBox.IsChecked == true;
        bool hasCarton = ChkHasCarton.IsChecked == true;

        var pieceName = PieceName;
        var boxName = BoxName;
        var cartonName = CartonName;

        if (hasBox && !hasPiece)
        {
            BoxQtyCol.Width = new GridLength(0);
            TxtBoxQty.Visibility = Visibility.Collapsed;
        }
        else
        {
            BoxQtyCol.Width = new GridLength(1, GridUnitType.Star);
            TxtBoxQty.Visibility = Visibility.Visible;
        }

        if (hasCarton && !hasBox && !hasPiece)
        {
            CartonQtyCol.Width = new GridLength(0);
            TxtCartonQty.Visibility = Visibility.Collapsed;
        }
        else
        {
            CartonQtyCol.Width = new GridLength(1, GridUnitType.Star);
            TxtCartonQty.Visibility = Visibility.Visible;
        }

        // Box label & hint
        if (hasPiece)
        {
            TxtBoxUnitLabel.Text = $"{boxName} تحتوي على: {pieceName}";
            TxtBoxHint.Text = $"* السعر يُحتسب تلقائياً من سعر {pieceName} × عدد {pieceName}";
            TxtBoxHint.Visibility = Visibility.Visible;
        }
        else
        {
            TxtBoxUnitLabel.Text = $"{boxName} - وحدة مستقلة";
            TxtBoxHint.Visibility = Visibility.Collapsed;
        }

        // Carton label & hint
        if (hasCarton)
        {
            if (hasBox)
            {
                TxtCartonUnitLabel.Text = $"{cartonName} تحتوي على: {boxName}";
                TxtCartonHint.Text = $"* السعر يُحتسب تلقائياً من سعر {boxName} × عدد {boxName}";
                TxtCartonHint.Visibility = Visibility.Visible;
            }
            else if (hasPiece)
            {
                TxtCartonUnitLabel.Text = $"{cartonName} تحتوي على: {pieceName} مباشرة";
                TxtCartonHint.Text = $"* السعر يُحتسب تلقائياً من سعر {pieceName} × عدد {pieceName}";
                TxtCartonHint.Visibility = Visibility.Visible;
            }
            else
            {
                TxtCartonUnitLabel.Text = $"{cartonName} - وحدة مستقلة";
                TxtCartonHint.Text = "* أدخل السعر يدوياً";
                TxtCartonHint.Visibility = Visibility.Visible;
            }
        }

        PieceMinField.Visibility = hasPiece ? Visibility.Visible : Visibility.Collapsed;
        BoxMinField.Visibility = hasBox ? Visibility.Visible : Visibility.Collapsed;
        CartonMinField.Visibility = hasCarton ? Visibility.Visible : Visibility.Collapsed;
    }

    private string PieceName => string.IsNullOrWhiteSpace(TxtPieceName?.Text) ? "قطعة" : TxtPieceName.Text.Trim();
    private string BoxName => string.IsNullOrWhiteSpace(TxtBoxName?.Text) ? "علبة" : TxtBoxName.Text.Trim();
    private string CartonName => string.IsNullOrWhiteSpace(TxtCartonName?.Text) ? "كرتونة" : TxtCartonName.Text.Trim();

    private void UpdateUnitLabels()
    {
        if (ChkHasPiece == null || ChkHasBox == null || ChkHasCarton == null) return;

        ChkHasPiece.Content = $"يوجد {PieceName}";
        ChkHasBox.Content = $"يوجد {BoxName}";
        ChkHasCarton.Content = $"يوجد {CartonName}";
        TxtPieceTitle.Text = PieceName;
        TxtBoxTitle.Text = BoxName;
        TxtCartonTitle.Text = CartonName;
        if (TxtPieceMinLabel != null)
            TxtPieceMinLabel.Text = $"({PieceName}):";
        if (TxtBoxMinLabel != null)
            TxtBoxMinLabel.Text = $"({BoxName}):";
        if (TxtCartonMinLabel != null)
            TxtCartonMinLabel.Text = $"({CartonName}):";
        UpdatePieceDependentFields();
    }

    private void UnitName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdateUnitLabels();
    }

    private void Qty_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || _isUpdating) return;
        UpdateAutoPrices();
    }

    private void Price_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || _isUpdating) return;

        var tb = (TextBox)sender;
        if (tb == TxtBoxRetail) _dirtyFields.Add("BoxRetail");
        else if (tb == TxtBoxWholesale) _dirtyFields.Add("BoxWholesale");
        else if (tb == TxtCartonRetail) _dirtyFields.Add("CartonRetail");
        else if (tb == TxtCartonWholesale) _dirtyFields.Add("CartonWholesale");

        UpdateAutoPrices();
    }

    private void UpdateAutoPrices()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        bool hasPiece = ChkHasPiece.IsChecked == true;
        bool hasBox = ChkHasBox.IsChecked == true;
        bool hasCarton = ChkHasCarton.IsChecked == true;

        decimal pieceRetail = 0, pieceWholesale = 0;
        bool pieceValid = hasPiece && TryParseDecimal(TxtPieceRetail.Text, out pieceRetail);
        if (hasPiece && !pieceValid)
        {
            _isUpdating = false;
            return;
        }
        if (pieceValid)
            pieceWholesale = TryParseDecimal(TxtPieceWholesale.Text, out decimal pw) ? pw : pieceRetail;

        bool boxQtyValid = false;
        int boxQty = 0;
        if (hasBox && int.TryParse(TxtBoxQty.Text, out boxQty) && boxQty > 0)
            boxQtyValid = true;

        bool cartonQtyValid = false;
        int cartonQty = 0;
        if (hasCarton && int.TryParse(TxtCartonQty.Text, out cartonQty) && cartonQty > 0)
            cartonQtyValid = true;

        // Box prices from piece
        if (hasBox && pieceValid && boxQtyValid)
        {
            if (!_dirtyFields.Contains("BoxRetail"))
                TxtBoxRetail.Text = (pieceRetail * boxQty).ToString("0.##");
            if (!_dirtyFields.Contains("BoxWholesale"))
                TxtBoxWholesale.Text = (pieceWholesale * boxQty).ToString("0.##");
        }

        // Carton prices
        if (hasCarton && cartonQtyValid)
        {
            bool cartonFromBox = hasBox;
            decimal boxRetail = 0, boxWholesale = 0;
            bool boxPricesValid = false;

            if (cartonFromBox)
            {
                boxPricesValid = TryParseDecimal(TxtBoxRetail.Text, out boxRetail);
                boxWholesale = TryParseDecimal(TxtBoxWholesale.Text, out decimal bw) ? bw : boxRetail;
            }

            if (cartonFromBox && boxPricesValid)
            {
                if (!_dirtyFields.Contains("CartonRetail"))
                    TxtCartonRetail.Text = (boxRetail * cartonQty).ToString("0.##");
                if (!_dirtyFields.Contains("CartonWholesale"))
                    TxtCartonWholesale.Text = (boxWholesale * cartonQty).ToString("0.##");
            }
            else if (pieceValid)
            {
                if (!_dirtyFields.Contains("CartonRetail"))
                    TxtCartonRetail.Text = (pieceRetail * cartonQty).ToString("0.##");
                if (!_dirtyFields.Contains("CartonWholesale"))
                    TxtCartonWholesale.Text = (pieceWholesale * cartonQty).ToString("0.##");
            }
        }

        _isUpdating = false;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveProduct())
            DialogClosed?.Invoke(this, true);
    }

    private void BtnSaveAndAdd_Click(object sender, RoutedEventArgs e)
    {
        if (SaveProduct())
        {
            NotificationManager.ShowSuccess("تم الحفظ — يمكنك إضافة منتج آخر");
            ResetForm(keepCategory: true);
        }
    }

    private void ResetForm(bool keepCategory = false)
    {
        _loaded = false;
        _dirtyFields.Clear();

        TxtName.Text = string.Empty;
        TxtDescription.Text = string.Empty;

        ChkHasPiece.IsChecked = false;
        TxtPieceName.Text = "قطعة";
        TxtPieceRetail.Text = string.Empty;
        TxtPieceWholesale.Text = string.Empty;

        ChkHasBox.IsChecked = true;
        TxtBoxName.Text = "علبة";
        TxtBoxQty.Text = string.Empty;
        TxtBoxRetail.Text = string.Empty;
        TxtBoxWholesale.Text = string.Empty;

        ChkHasCarton.IsChecked = false;
        TxtCartonName.Text = "كرتونة";
        TxtCartonQty.Text = string.Empty;
        TxtCartonRetail.Text = string.Empty;
        TxtCartonWholesale.Text = string.Empty;

        if (!keepCategory)
        {
            _selectedCategoryId = null;
            TxtCategorySearch.Text = string.Empty;
        }

        // دائماً نعيد رسم الـ chips وتحديث منتجات القسم
        RefreshChips();
        LoadCategoryProducts();

        _loaded = true;
        TxtName.Focus();
    }

    private bool SaveProduct()
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text) || TxtName.Text == ProductApp.Converters.WatermarkBehavior.GetWatermark(TxtName))
        {
            NotificationManager.ShowError("الرجاء إدخال اسم المنتج");
            return false;
        }

        var name = TxtName.Text.Trim();
        var excludeId = _product?.Id ?? 0;
        if (_db.Products.Any(p => p.Name == name && p.Id != excludeId))
        {
            NotificationManager.ShowError("هذا الاسم موجود بالفعل");
            return false;
        }

        bool hasPiece = ChkHasPiece.IsChecked == true;
        bool hasBox = ChkHasBox.IsChecked == true;
        bool hasCarton = ChkHasCarton.IsChecked == true;

        if (!hasPiece && !hasBox && !hasCarton)
        {
            NotificationManager.ShowError($"الرجاء اختيار نوع تعبئة واحد على الأقل ({PieceName}، {BoxName}، {CartonName})");
            return false;
        }

        if (hasPiece && (string.IsNullOrWhiteSpace(TxtPieceRetail.Text) || !TryParseDecimal(TxtPieceRetail.Text, out _)))
        {
            NotificationManager.ShowError($"الرجاء إدخال سعر القطاعي لـ{PieceName}");
            return false;
        }
        if (hasBox && (string.IsNullOrWhiteSpace(TxtBoxRetail.Text) || !TryParseDecimal(TxtBoxRetail.Text, out _)))
        {
            NotificationManager.ShowError($"الرجاء إدخال سعر القطاعي لـ{BoxName}");
            return false;
        }
        if (hasCarton && (string.IsNullOrWhiteSpace(TxtCartonRetail.Text) || !TryParseDecimal(TxtCartonRetail.Text, out _)))
        {
            NotificationManager.ShowError($"الرجاء إدخال سعر القطاعي لـ{CartonName}");
            return false;
        }

        Product product;
        if (_product != null)
        {
            product = _db.Products.Find(_product!.Id)!;
            var oldUnits = _db.ProductUnits.Where(u => u.ProductId == product.Id).ToList();
            _db.ProductUnits.RemoveRange(oldUnits);
        }
        else
        {
            product = new Product();
            _db.Products.Add(product);
        }

        product.Name = name;
        product.Description = TxtDescription.Text?.Trim();
        product.CategoryId = _selectedCategoryId;
        _db.SaveChanges();

        ProductUnit? pieceUnit = null;
        ProductUnit? boxUnit = null;
        ProductUnit? cartonUnit = null;

        if (hasPiece)
        {
            TryParseDecimal(TxtPieceRetail.Text, out decimal pieceRetail);
            decimal pieceWholesale = TryParseDecimal(TxtPieceWholesale.Text, out decimal pw) ? pw : pieceRetail;
            pieceUnit = new ProductUnit
            {
                ProductId = product.Id,
                Name = string.IsNullOrWhiteSpace(TxtPieceName.Text) ? "قطعة" : TxtPieceName.Text.Trim(),
                UnitType = UnitType.Piece,
                RetailPrice = pieceRetail,
                WholesalePrice = pieceWholesale,
                MinStockLevel = int.TryParse(TxtPieceMin.Text?.Trim(), out int pieceMin) && pieceMin > 0 ? pieceMin : 0,
                IsBaseUnit = !hasBox && !hasCarton,
                QuantityPerParent = 1
            };
            _db.ProductUnits.Add(pieceUnit);
            _db.SaveChanges();
        }

        if (hasBox)
        {
            bool boxQtyValid = int.TryParse(TxtBoxQty.Text, out int boxQty) && boxQty > 0;
            decimal boxRetail = TryParseDecimal(TxtBoxRetail.Text, out decimal br) ? br : 0;
            decimal boxWholesale = TryParseDecimal(TxtBoxWholesale.Text, out decimal bw) ? bw : boxRetail;
            boxUnit = new ProductUnit
            {
                ProductId = product.Id,
                Name = string.IsNullOrWhiteSpace(TxtBoxName.Text) ? "علبة" : TxtBoxName.Text.Trim(),
                UnitType = UnitType.Box,
                RetailPrice = boxRetail,
                WholesalePrice = boxWholesale,
                MinStockLevel = int.TryParse(TxtBoxMin.Text?.Trim(), out int boxMin) && boxMin > 0 ? boxMin : 0,
                QuantityPerParent = boxQtyValid ? boxQty : 1,
                IsBaseUnit = !hasPiece && !hasCarton
            };
            _db.ProductUnits.Add(boxUnit);
            _db.SaveChanges();
        }

        if (hasCarton)
        {
            bool cartonQtyValid = int.TryParse(TxtCartonQty.Text, out int cartonQty) && cartonQty > 0;
            decimal cartonRetail = TryParseDecimal(TxtCartonRetail.Text, out decimal cr) ? cr : 0;
            decimal cartonWholesale = TryParseDecimal(TxtCartonWholesale.Text, out decimal cw) ? cw : cartonRetail;
            cartonUnit = new ProductUnit
            {
                ProductId = product.Id,
                Name = string.IsNullOrWhiteSpace(TxtCartonName.Text) ? "كرتونة" : TxtCartonName.Text.Trim(),
                UnitType = UnitType.Carton,
                RetailPrice = cartonRetail,
                WholesalePrice = cartonWholesale,
                MinStockLevel = int.TryParse(TxtCartonMin.Text?.Trim(), out int cartonMin) && cartonMin > 0 ? cartonMin : 0,
                QuantityPerParent = cartonQtyValid ? cartonQty : 1,
                IsBaseUnit = !hasPiece && !hasBox
            };
            _db.ProductUnits.Add(cartonUnit);
            _db.SaveChanges();
        }

        // Link hierarchy
        if (pieceUnit != null)
        {
            if (boxUnit != null)
            {
                pieceUnit.ParentUnitId = boxUnit.Id;
                if (cartonUnit != null) boxUnit.ParentUnitId = cartonUnit.Id;
            }
            else if (cartonUnit != null)
                pieceUnit.ParentUnitId = cartonUnit.Id;
        }
        else if (boxUnit != null && cartonUnit != null)
            boxUnit.ParentUnitId = cartonUnit.Id;

        _db.SaveChanges();
        return true;
    }

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogClosed?.Invoke(this, false);
    }
}

public class SimpleCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

