using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using ProductApp.Data;
using ProductApp.Models;
using ProductApp.Services;

namespace ProductApp.Views;

public partial class CategoryDialog : UserControl
{
    public event EventHandler<bool?>? DialogClosed;

    private readonly AppDbContext _db;
    private readonly Category? _editCategory;

    public CategoryDialog(AppDbContext db, Category? category = null)
    {
        InitializeComponent();
        _db = db;
        _editCategory = category;

        LoadParentCategories();

        if (category != null)
        {
            TxtHeader.Text = "تعديل القسم";
            TxtName.Text = category.Name;
            TxtDescription.Text = category.Description;
            BtnSave.Content = "حفظ التعديلات";
        }
    }

    private void LoadParentCategories()
    {
        var excludeId = _editCategory?.Id ?? 0;
        var cats = _db.Categories
            .Where(c => c.Id != excludeId)
            .OrderBy(c => c.Name)
            .ToList();

        cats.Insert(0, new Category { Id = 0, Name = "-- بدون قسم أب --" });
        CmbParentCategory.ItemsSource = cats;
        CmbParentCategory.SelectedIndex = 0;

        if (_editCategory?.ParentCategoryId != null)
        {
            var idx = cats.FindIndex(c => c.Id == _editCategory.ParentCategoryId);
            if (idx >= 0) CmbParentCategory.SelectedIndex = idx;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            NotificationManager.ShowError("الرجاء إدخال اسم القسم");
            return;
        }

        var name = TxtName.Text.Trim();
        var excludeId = _editCategory?.Id ?? 0;
        if (_db.Categories.Any(c => c.Name == name && c.Id != excludeId))
        {
            NotificationManager.ShowError("هذا الاسم موجود بالفعل");
            return;
        }

        Category category;
        if (_editCategory != null)
        {
            category = _editCategory;
            _db.Attach(category);
        }
        else
        {
            category = new Category();
            _db.Categories.Add(category);
        }

        category.Name = name;
        category.Description = TxtDescription.Text?.Trim();

        var selectedParent = CmbParentCategory.SelectedItem as Category;
        category.ParentCategoryId = selectedParent?.Id > 0 ? selectedParent.Id : null;

        _db.SaveChanges();
        DialogClosed?.Invoke(this, true);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogClosed?.Invoke(this, false);
    }
}