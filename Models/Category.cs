using System.ComponentModel.DataAnnotations;

namespace ProductApp.Models;

public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> ChildCategories { get; set; } = new List<Category>();

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}