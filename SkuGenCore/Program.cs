using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WebApiCore.MySqlModels;
using WhiteHingeFramework.Classes.Items;
using Inventory = WebApiCore.MySqlModels.Inventory;

namespace SkuGenCore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Console.TreatControlCAsInput = true;
            var results = SkuGeneration.GenerateListOfSku().Result;
            Console.ReadKey();
        }
    }

    public static class SkuGeneration
    {
        public static async Task<NewWhlSku> CreateSku(Whlnew mySqlData)
        {
            await Task.Delay(0);
            Console.WriteLine("Loading Basic Data");
            var newSku = new NewWhlSku();
            newSku.Sku = mySqlData.Sku;
            newSku.Packsize = Convert.ToInt32(mySqlData.Packsize);
            newSku.RetailPrice = Convert.ToDecimal(mySqlData.Retail);
            var barcodes = new HashSet<string>();
            newSku.PriceData = new NewWhlSku.SkuPriceData
            {
                Gross = Convert.ToDecimal(mySqlData.Gross),
                Net = Convert.ToDecimal(mySqlData.Net),
                Retail = Convert.ToDecimal(mySqlData.Retail)
            };
            decimal.TryParse(mySqlData.Profit, out newSku.PriceData.Profit);
            decimal.TryParse(mySqlData.Margin, out newSku.PriceData.Margin);

            //Todo Pieces
            //Todo DeliveryNote
            
            Console.WriteLine("Loading Stock Levels");
            var inv = GetStockLevels(mySqlData.Sku);
            newSku.StockData = new NewWhlSku.SkuStockData();
            int.TryParse(inv.Stock, out newSku.StockData.Level);
            int.TryParse(inv.Stockminimum, out newSku.StockData.Minimum);
            
            Console.WriteLine("Loading Locations");
            newSku.Locations = newSku.GetShortSkuLocations();
            
            Console.WriteLine("Loading Suppliers");
            newSku.Suppliers = newSku.GetSuppliers();

            Console.WriteLine("Loading SalesData");
            return newSku;
        }

        public static Inventory GetStockLevels(string sku)
        {
            using (var context = new whldataContext())
            {
                return context.Inventory.FirstOrDefault(x => x.Sku == sku);
            }
        }

        public static List<LocationData> GetShortSkuLocations(this NewWhlSku item)
        {
            var returnable = new List<LocationData>();
            using (var context = new whldataContext())
            {
                var rows = from x in context.ShortskuLocations
                    join locRef in context.Locationreference on x.LocationId equals locRef.LocId
                    select new
                    {
                        Id = x.Id,
                        Shelfname = x.Shelfname,
                        ShortSku = x.Shortsku,
                        LocationId = x.LocationId,
                        Warehouse = locRef.LocWarehouse,
                        PickRoute = locRef.RouteId,
                        StockLevel = x.StockLevel,
                        locRef.LocType
                    };//Selects the required types from the database
                foreach (var row in rows) //Iterates through and adds them back to the collection
                {
                    var location = new LocationData
                    {
                        SkuLocationId = row.Id,
                        ShelfName = row.Shelfname,
                        Sku = row.ShortSku,
                        LocationReferenceId = row.LocationId,
                        LocationWarehouse = ((Warehouse) row.Warehouse).ToString(),
                        LocWarehouse = (Warehouse) row.Warehouse,
                        PickRoute = row.PickRoute,
                        LocType = (LocationType) row.LocType
                    };
                    returnable.Add(location);
                }
                
            }
            return returnable;
        }

        public static List<NewSkuSupplier> GetSuppliers(this NewWhlSku item)
        {

            var returnable = new List<NewSkuSupplier>();
            IEnumerable<SkuSupplierdata> supplierData;
            using (var context = new whldataContext())
            {
                supplierData = context.SkuSupplierdata.Where(x => x.Sku == item.ShortSku && x.Invisible == "False");
            }
            foreach (var supplier in supplierData)
            {
                var supp = new NewSkuSupplier
                {
                    SupplierName = supplier.SupplierName,
                    SupplierCode =  supplier.SupplierCode,
                    BarcodeDictionary = new Dictionary<string, string>
                    {
                        {"Item", supplier.SupplierBarcode},
                        {"Inner", supplier.SupplierCaseInnerBarcode},
                        {"Outer", supplier.SupplierCaseBarcode},
                        {"Reorder", supplier.SupplierCode}
                    }
                };
                float.TryParse(supplier.SupplierPricePer,out supp.SupplierPrice);
                bool.TryParse(supplier.IsPrimary, out supp.SupplierIsPrimary);
                bool.TryParse(supplier.IsDiscontinued, out supp.SupplierIsDiscontinued);
                bool.TryParse(supplier.IsOutOfStock, out supp.SupplierOutOfStock);

                returnable.Add(supp);
            }
            return returnable;
        }
        

        public static async Task<List<NewWhlSku>> GenerateListOfSku()
        {
            List<Whlnew> Skus;
            var resultSkus = new ConcurrentBag<NewWhlSku>();
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            Console.WriteLine("Loading Skus");
            using (var context = new whldataContext())
            {
                Skus = await context.Whlnew.Where(x => x.NewStatus != "Dead").ToListAsync();
                Console.WriteLine($"Loaded {Skus.Count} Skus");
            }
            
            Console.WriteLine("Starting Generation");
            Parallel.ForEach(Skus, new ParallelOptions {MaxDegreeOfParallelism = 1}, (x) => //Todo Increase MaxDegreeOfParallelism
            {
                try
                {
                    var result = CreateSku(x).Result;
                    Console.WriteLine($"Generated {result.Sku}");
                    resultSkus.Add(result);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

            });
            
            var returnable = resultSkus.OrderBy(x => x.Sku).ThenBy(x => x.Packsize).ToList();
            Console.WriteLine($"Generated {returnable.Count()} Skus in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"{stopwatch.Elapsed.Seconds} Seconds");
            return returnable;
        }
    }

    public static class SkuCollectionExtensions
    {
        public static List<NewWhlSku> SearchBarcodes(this List<NewWhlSku> collection,string searchTerm)
        {
            var returnable = new List<NewWhlSku>();
            if (searchTerm == "") return returnable;
            returnable.AddRange(collection.Where(x => x.Barcodes.Any(y => y == searchTerm)));
            return returnable;
        }
    }
}