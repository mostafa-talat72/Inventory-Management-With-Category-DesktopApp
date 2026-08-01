using Microsoft.EntityFrameworkCore;
using ProductApp.Models;
using System.IO;

namespace ProductApp.Data;

public class AppDbContext : DbContext
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    private static readonly string DbFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MTE Stock");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!Directory.Exists(DbFolder))
            Directory.CreateDirectory(DbFolder);
        var dbPath = Path.Combine(DbFolder, "inventory.db");
        options.UseSqlite($"Data Source={dbPath}");
    }

    public static void MigrateIfNeeded()
    {
        using var db = new AppDbContext();
        db.Database.EnsureCreated();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        // 1) IsCostRecovered column
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(InventoryMovements)";
            using var reader = checkCmd.ExecuteReader();
            var hasCol = false;
            while (reader.Read())
                if ((string)reader["name"] == "IsCostRecovered") { hasCol = true; break; }
            if (!hasCol)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE InventoryMovements ADD COLUMN IsCostRecovered INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
        }

        // 2) Categories table (for existing databases)
        using (var checkCat = conn.CreateCommand())
        {
            checkCat.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Categories'";
            var exists = (long)checkCat.ExecuteScalar()!;
            if (exists == 0)
            {
                using var create = conn.CreateCommand();
                create.CommandText = @"
CREATE TABLE IF NOT EXISTS Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    ParentCategoryId INTEGER,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (ParentCategoryId) REFERENCES Categories(Id) ON DELETE RESTRICT
)";
                create.ExecuteNonQuery();
            }
        }

        // 3) CategoryId column in Products
        using (var checkProd = conn.CreateCommand())
        {
            checkProd.CommandText = "PRAGMA table_info(Products)";
            using var reader = checkProd.ExecuteReader();
            var hasCatCol = false;
            while (reader.Read())
                if ((string)reader["name"] == "CategoryId") { hasCatCol = true; break; }
            if (!hasCatCol)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE Products ADD COLUMN CategoryId INTEGER REFERENCES Categories(Id) ON DELETE SET NULL";
                alter.ExecuteNonQuery();
            }
        }

        // 4) MinStockLevel column in ProductUnits
        using (var checkMin = conn.CreateCommand())
        {
            checkMin.CommandText = "PRAGMA table_info(ProductUnits)";
            using var reader = checkMin.ExecuteReader();
            var hasMinCol = false;
            while (reader.Read())
                if ((string)reader["name"] == "MinStockLevel") { hasMinCol = true; break; }
            if (!hasMinCol)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE ProductUnits ADD COLUMN MinStockLevel INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
        }

        // 5) Barcode column in Products
        using (var checkBarcode = conn.CreateCommand())
        {
            checkBarcode.CommandText = "PRAGMA table_info(Products)";
            using var reader = checkBarcode.ExecuteReader();
            var hasBarcode = false;
            while (reader.Read())
                if ((string)reader["name"] == "Barcode") { hasBarcode = true; break; }
            if (!hasBarcode)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE Products ADD COLUMN Barcode TEXT";
                alter.ExecuteNonQuery();
            }
        }

        // 6) IsFavorite column in Products
        using (var checkFav = conn.CreateCommand())
        {
            checkFav.CommandText = "PRAGMA table_info(Products)";
            using var reader = checkFav.ExecuteReader();
            var hasFav = false;
            while (reader.Read())
                if ((string)reader["name"] == "IsFavorite") { hasFav = true; break; }
            if (!hasFav)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE Products ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Category>()
            .HasOne(c => c.ParentCategory)
            .WithMany(c => c.ChildCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<ProductUnit>()
            .HasOne(u => u.ParentUnit)
            .WithMany(u => u.ChildUnits)
            .HasForeignKey(u => u.ParentUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Invoice>()
            .HasOne(i => i.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
