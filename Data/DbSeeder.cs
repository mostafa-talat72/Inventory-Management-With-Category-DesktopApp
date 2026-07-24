using ProductApp.Models;

namespace ProductApp.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Products.Any()) return;

        // Seed categories
        var sportsCat = new Category { Name = "?????", Description = "???????? ????????" };
        var footballCat = new Category { Name = "??? ???" };
        var tennisCat = new Category { Name = "???" };
        sportsCat.ChildCategories.Add(footballCat);
        sportsCat.ChildCategories.Add(tennisCat);
        var beveragesCat = new Category { Name = "???????", Description = "????????? ???????" };

        db.Categories.AddRange(sportsCat, beveragesCat);
        db.SaveChanges();

        // Re-fetch with IDs assigned
        var sports = db.Categories.First(c => c.Name == "?????");
        var football = db.Categories.First(c => c.Name == "??? ???");
        var tennis = db.Categories.First(c => c.Name == "???");
        var beverages = db.Categories.First(c => c.Name == "???????");

        db.Customers.AddRange(
            new Customer { Name = "???? ????", Phone = "01012345678", Address = "???????" },
            new Customer { Name = "???? ???", Phone = "01123456789", Address = "??????????" },
            new Customer { Name = "??? ???", Phone = "01234567890", Address = "??????" },
            new Customer { Name = "???? ????", Phone = "01556789012", Address = "????????" }
        );

        db.SaveChanges();

        var ball = new Product { Name = "??? ??? ??????", Description = "??? ??? ???? 5", CategoryId = football.Id };
        var bat = new Product { Name = "???? ???", Description = "???? ???????", CategoryId = tennis.Id };
        var water = new Product { Name = "???? ?????? 1.5 ???", Description = "???? ????", CategoryId = beverages.Id };

        db.Products.AddRange(ball, bat, water);
        db.SaveChanges();

        // ??? ???: ?????? ? ???? (12 ????) ? ????
        db.ProductUnits.AddRange(
            new ProductUnit { Product = ball, Name = "????", UnitType = UnitType.Piece, RetailPrice = 50, WholesalePrice = 45, IsBaseUnit = true, QuantityPerParent = 1 },
            new ProductUnit { Product = ball, Name = "???? (12 ???)", UnitType = UnitType.Box, RetailPrice = 500, WholesalePrice = 480, QuantityPerParent = 12 },
            new ProductUnit { Product = ball, Name = "?????? (10 ???)", UnitType = UnitType.Carton, RetailPrice = 4500, WholesalePrice = 4200, QuantityPerParent = 10 }
        );

        // ???? ???: ?????? ? ???? ??????
        db.ProductUnits.AddRange(
            new ProductUnit { Product = bat, Name = "????", UnitType = UnitType.Piece, RetailPrice = 200, WholesalePrice = 180, IsBaseUnit = true, QuantityPerParent = 1 },
            new ProductUnit { Product = bat, Name = "?????? (12 ????)", UnitType = UnitType.Carton, RetailPrice = 2000, WholesalePrice = 1900, QuantityPerParent = 12 }
        );

        // ????: ?????? ??? (???? ?????????)
        db.ProductUnits.AddRange(
            new ProductUnit { Product = water, Name = "?????? (12 ?????)", UnitType = UnitType.Piece, RetailPrice = 60, WholesalePrice = 55, IsBaseUnit = true, QuantityPerParent = 1 },
            new ProductUnit { Product = water, Name = "?????? (12 ?????)", UnitType = UnitType.Carton, RetailPrice = 60, WholesalePrice = 55, QuantityPerParent = 12 }
        );

        // Fix parent-child relationships
        db.SaveChanges();

        var ballUnits = db.ProductUnits.Where(u => u.ProductId == ball.Id).ToList();
        var ballCarton = ballUnits.First(u => u.UnitType == UnitType.Carton);
        var ballBox = ballUnits.First(u => u.UnitType == UnitType.Box);
        var ballPiece = ballUnits.First(u => u.UnitType == UnitType.Piece);

        ballBox.ParentUnitId = ballCarton.Id;
        ballPiece.ParentUnitId = ballBox.Id;

        var batUnits = db.ProductUnits.Where(u => u.ProductId == bat.Id).ToList();
        var batCarton = batUnits.First(u => u.UnitType == UnitType.Carton);
        var batPiece = batUnits.First(u => u.UnitType == UnitType.Piece);
        batPiece.ParentUnitId = batCarton.Id;

        var waterUnits = db.ProductUnits.Where(u => u.ProductId == water.Id).ToList();
        var waterCarton = waterUnits.First(u => u.UnitType == UnitType.Carton);
        var waterPiece = waterUnits.First(u => u.UnitType == UnitType.Piece);
        waterPiece.ParentUnitId = waterCarton.Id;

        db.SaveChanges();

        // Seed inventory
        db.InventoryBatches.AddRange(
            new InventoryBatch { ProductId = ball.Id, CostPricePerPiece = 30, InitialQuantity = 1200, RemainingQuantity = 1200, PurchaseDate = DateTime.Now.AddDays(-30) },
            new InventoryBatch { ProductId = ball.Id, CostPricePerPiece = 32, InitialQuantity = 600, RemainingQuantity = 600, PurchaseDate = DateTime.Now.AddDays(-15) },
            new InventoryBatch { ProductId = bat.Id, CostPricePerPiece = 120, InitialQuantity = 240, RemainingQuantity = 240, PurchaseDate = DateTime.Now.AddDays(-20) },
            new InventoryBatch { ProductId = water.Id, CostPricePerPiece = 35, InitialQuantity = 5000, RemainingQuantity = 5000, PurchaseDate = DateTime.Now.AddDays(-10) }
        );

        db.SaveChanges();
    }
}
