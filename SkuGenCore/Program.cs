using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProtoBuf;
using ShellProgressBar;
using WebApiCore.MySqlModels;
using WhiteHingeFramework.Classes.Items;
using Inventory = WebApiCore.MySqlModels.Inventory;

namespace SkuGenCore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Loading the Application");
            Console.TreatControlCAsInput = true;
            Console.ForegroundColor = ConsoleColor.Green;
            var currentAsm = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var restartApp = false;
            do
            {
                Console.Clear();
                Console.WriteLine("Starting Sku Generation");
                RunSkuGeneration();
                if (DateTime.Now.Hour % 2 != 0 && DateTime.Now.Minute > 10 && DateTime.Now.Minute < 20) restartApp = true;
                else
                {
                    Console.WriteLine("Sleeping for 1 Minute");
                    Thread.Sleep(new TimeSpan(0,1,0));
                }
            } while (!restartApp);
            Console.WriteLine("Restarting Application");
            Process.Start("dotnet", $"run {currentAsm}");
            
            return;
        }

        public static void RunSkuGeneration()
        {
            var results = GenerateListOfSku().Result;
            for (int i = 0; i >= 10; i++)
            {
                
            
                if (results != null)
                {
                    File.Delete($@"C:\Memes\Skus{i}.collection");
                    
                    using (var file = File.Open($@"C:\Memes\Skus{i}.collection", FileMode.OpenOrCreate))
                    {
                        Serializer.Serialize(file, results);
                    }
                }
            }

        }
        
        
        public static async Task<List<NewWhlSku>> GenerateListOfSku()
        {
            List<Whlnew> skus;
            var resultSkus = new ConcurrentBag<NewWhlSku>();
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            List<WarehouseIssueLog> precompiledIssueLogs;
            Console.WriteLine("Loading Skus");
            using (var context = new whldataContext())
            {
                skus = await context.Whlnew.Where(x => x.NewStatus != "Dead").ToListAsync();
                Console.WriteLine($"Loaded {skus.Count} Skus");
                precompiledIssueLogs = context.WarehouseIssueLog.Where(x => x.Sku.Length != 0).ToList();
            }
            Console.WriteLine("Starting Generation");
            try
            {
                int totalTicks = skus.Count;
                var options = new ProgressBarOptions
                {
                    ProgressCharacter = '─',
                    ProgressBarOnBottom = true,
                    ForegroundColor = ConsoleColor.Green
                };
                var progress = new ProgressBar(totalTicks, "Generating Skus", options);

                Parallel.ForEach(skus, new ParallelOptions {MaxDegreeOfParallelism = 4},(x) => 
                    {
                        try
                        {
                            var issues = precompiledIssueLogs.Where(y => y.Sku.Substring(0, 7) == x.Shortsku).ToList();
                            var result = SkuGeneration.CreateSku(x, issues).Result;
                            //Console.WriteLine($"Generated {result.Sku}");

                            resultSkus.Add(result);
                            //Console.WriteLine($"{resultSkus.Count} complete of {skus.Count}");
                            TickToCompletion(progress, totalTicks, 1);
                            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
                        }
                        catch (Exception e)
                        {
                            e.Data.Add("Sku", x.Sku);
                            Console.WriteLine(e);
                            throw;
                        }
                    });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine($"Sku: {e.Data["Sku"]}");
                Console.WriteLine("Press Escape to Quit");
                return null;
            }
            
            
            var temp = resultSkus.ToList();
            var returnable = temp.OrderBy(x => x.Sku).ThenBy(x => x.Packsize).ToList();
            Console.WriteLine($"Generated {returnable.Count()} Skus in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"{stopwatch.Elapsed.Seconds} Seconds");
            return returnable;
        }
        public static void TickToCompletion(IProgressBar pbar, int ticks, int sleep = 1750, Action childAction = null)
        {
            var initialMessage = pbar.Message;
            for (var i = 0; i < ticks; i++)
            {
                pbar.Message = $"Start {i + 1} of {ticks}: {initialMessage}";
                childAction?.Invoke();
                Thread.Sleep(sleep);
                pbar.Tick($"End {i + 1} of {ticks}: {initialMessage}");
            }
        }

    }

    public static class SkuGeneration
    {
        public static async Task<NewWhlSku> CreateSku(Whlnew mySqlData,List<WarehouseIssueLog> issueLog,bool skipBundles = false)
        {
            await Task.Delay(0);
            var newSku = new NewWhlSku
            {
                Sku = mySqlData.Sku,
                Packsize = Convert.ToInt32(mySqlData.Packsize),
                RetailPrice = Convert.ToDecimal(mySqlData.Retail),
                DistinguishTitle = mySqlData.Ext35,
                NoteData = new SkuNoteData
                {
                    DeliveryNote = mySqlData.Deliverynote,
                    PickNote = mySqlData.PickNote,
                    KnowledgeNote = mySqlData.KnowledgeNote
                },
                PriceData = new SkuPriceData
                {
                    Gross = Convert.ToDecimal(mySqlData.Gross),
                    Net = Convert.ToDecimal(mySqlData.Net),
                    Retail = Convert.ToDecimal(mySqlData.Retail)
                },
                Titles = new SkuExtendedTitles
                {
                    Invoice = mySqlData.Itemtitle,
                    Label = mySqlData.Labelshort,
                    Linnworks = mySqlData.Linnshort,
                    Distinguish = mySqlData.Ext35,
                    NewItem = mySqlData.NewDescription
                },
                ItemData = new ItemProfile
                {
                    Brand = mySqlData.NewBrand,
                    Description = mySqlData.NewDescription,
                    Finish = mySqlData.NewFinish,
                    Category = mySqlData.Ext33,
                    Size = mySqlData.NewSize,
                    Note = mySqlData.NewNote,
                    Box = mySqlData.NewTransferBox,
                },
                ExtendedData = new SkuExtendedData
                {
                    Envelope = mySqlData.Envelope,
                    LabourCode = mySqlData.Labour,
                    Status = mySqlData.NewStatus
                },
                NewData = new NewSkuData
                {
                    GS1Barcode = mySqlData.Gs1
                },
                CostData = new SkuCostData(),
                BundleData = new SkuBundleData(),
                LinnworksInfo = new SkuLinnworksInfo()
            };
            if (newSku.NewData.GS1Barcode == null)
            {
                newSku.NewData.GS1Barcode = "";
            }
            newSku.Packsize = Convert.ToInt32(mySqlData.Packsize);
            //Item Profile
            int.TryParse(mySqlData.Parts, out newSku.ItemData.Parts);
            int.TryParse(mySqlData.Pieces, out newSku.ItemData.Pieces);
            int.TryParse(mySqlData.Screws, out newSku.ItemData.Screws);
            decimal.TryParse(mySqlData.Weight.ToString(), out newSku.ItemData.Weight);
            //ExtendedData
            bool.TryParse(mySqlData.IsPair, out newSku.ExtendedData.IsPair);
            bool.TryParse(mySqlData.IsListed, out newSku.ExtendedData.IsListed);
            bool.TryParse(mySqlData.HasBeenListed, out newSku.ExtendedData.HasBeenListed);
            bool.TryParse(mySqlData.Courier, out newSku.ExtendedData.Courier);
            bool.TryParse(mySqlData.OversizedItem, out newSku.ExtendedData.Oversized);
            bool.TryParse(mySqlData.IsBundle, out newSku.ExtendedData.IsBundle);
            //Pricing Data
            decimal.TryParse(mySqlData.Profit, out newSku.PriceData.Profit);
            decimal.TryParse(mySqlData.Margin, out newSku.PriceData.Margin);
            //Cost Data
            decimal.TryParse(mySqlData.Packingcost, out newSku.CostData.Packing);
            decimal.TryParse(mySqlData.Postagecost, out newSku.CostData.Postage);
            decimal.TryParse(mySqlData.Envcost, out newSku.CostData.Envelope);
            decimal.TryParse(mySqlData.Feescost, out newSku.CostData.Fees);
            decimal.TryParse(mySqlData.Labourcost, out newSku.CostData.Labour);
            decimal.TryParse(mySqlData.Vatcost, out newSku.CostData.Vat);
            decimal.TryParse(mySqlData.Totalcost, out newSku.CostData.Total);
            //Linnworks Data
            int.TryParse(mySqlData.Initiallevel, out newSku.LinnworksInfo.InitialLevel);
            int.TryParse(mySqlData.Initialquantity, out newSku.LinnworksInfo.InitialStock);
            int.TryParse(mySqlData.InitMinimum, out newSku.LinnworksInfo.InitialMinimum);
            int.TryParse(mySqlData.ListPriority, out newSku.LinnworksInfo.ListPriority);

            
            
            
            var inv = GetStockLevels(mySqlData.Sku);
            newSku.StockData = new SkuStockData();
            int.TryParse(inv.Stock, out newSku.StockData.Level);
            int.TryParse(inv.Stockminimum, out newSku.StockData.Minimum);
            using (var context = new whldataContext())
            {
                newSku.Locations = newSku.GetShortSkuLocations(context);
            
                newSku.Suppliers = newSku.GetSuppliers(context);

                newSku.SalesData = newSku.GetSkuSalesData(context);
                newSku.PrepackInfo = newSku.GetSkuPrepackInfo(context);

                //newSku.UpdateOrderwiseData(context);
                if (issueLog.Count != 0)
                {
                    newSku.GetMissedPicksAndIssues(issueLog);
                }
                else newSku.NewData.MissedPicks = new MispickStatistic();

                if (newSku.ExtendedData.IsBundle && !skipBundles)
                {
                    newSku.BundleData = newSku.GetSkuBundleInfo(context,mySqlData,true);
                }
            }
            newSku.Barcodes = newSku.GetSkuBarcodes();
            newSku.SearchKeywords = newSku.GetSearchKeywords();

            return newSku;
        }

        public static Inventory GetStockLevels(string sku)
        {
            using (var context = new whldataContext())
            {
                var result = context.Inventory.FirstOrDefault(x => x.Sku == sku);
                if (result != null) return result;
                
                return new Inventory();
            }
        }

        public static List<LocationData> GetShortSkuLocations(this NewWhlSku item,whldataContext context)
        {
            var returnable = new List<LocationData>();

                var rows = from x in context.ShortskuLocations
                    join locRef in context.Locationreference on x.LocationId equals locRef.LocId
                    where x.Shortsku == item.ShortSku
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
                
            
            return returnable;
        }

        public static List<NewSkuSupplier> GetSuppliers(this NewWhlSku item,whldataContext context)
        {
            var returnable = new List<NewSkuSupplier>();
            IEnumerable<SkuSupplierdata> supplierData =
                context.SkuSupplierdata.Where(x => x.Sku == item.ShortSku && x.Invisible == "False");

            foreach (var supplier in supplierData)
            {
                var supp = new NewSkuSupplier
                {
                    SupplierName = supplier.SupplierName,
                    SupplierCode = supplier.SupplierCode,
                    BarcodeDictionary = new Dictionary<string, string>
                    {
                        {"Item", supplier.SupplierBarcode},
                        {"Inner", supplier.SupplierCaseInnerBarcode},
                        {"Outer", supplier.SupplierCaseBarcode},
                        {"Reorder", supplier.SupplierCode}
                    }
                };
                float.TryParse(supplier.SupplierPricePer, out supp.SupplierPrice);
                bool.TryParse(supplier.IsPrimary, out supp.SupplierIsPrimary);
                bool.TryParse(supplier.IsDiscontinued, out supp.SupplierIsDiscontinued);
                bool.TryParse(supplier.IsOutOfStock, out supp.SupplierOutOfStock);
                
                returnable.Add(supp);
            }
            return returnable;
        }

        public static SkuSalesData GetSkuSalesData(this NewWhlSku item, whldataContext context)
        {
            var results = context.Salesdata.First(x => x.Sku == item.Sku);
            return new SkuSalesData
            {
                Avg1 = int.Parse(results.Avg1Week.ToString()),
                Avg4 = Convert.ToInt32(results.Avg4Week.ToString()),
                Avg8 = Convert.ToInt32(results.Avg8Week.ToString()),
                Raw1 = Convert.ToInt32(results.Raw1WeekTotal.ToString()),
                Raw4 = Convert.ToInt32(results.Raw4WeekTotal.ToString()),
                Raw8 = Convert.ToInt32(results.Raw8WeekTotal.ToString()),
                Weighted = Convert.ToInt32(results.Weighted8Week.ToString())
            };
            
        }

        public static SkuPrepackInfo GetSkuPrepackInfo(this NewWhlSku item, whldataContext context)
        {
            var result = context.Prepacklist.FirstOrDefault(x => x.Sku == item.Sku);
            if (result == null) return new SkuPrepackInfo();
            else
            {
                return new SkuPrepackInfo
                {
                    Bag = result.Bag,
                    Notes = result.Notes
                };
            }
        }

        public static SkuBundleData GetSkuBundleInfo(this NewWhlSku item, whldataContext context,Whlnew baseData,bool isNewSku = false)
        {
            if(!item.ExtendedData.IsBundle) return new SkuBundleData(); //It's not a bundle return a default collection
            
            var compositionSkus = context.SkuComposition.Where(x => x.BundleSku == item.ShortSku); //Find all the skus in the bundle
            
            var returnable = new SkuBundleData();
            
            bool.TryParse(baseData.SplitBundle, out returnable.SplitBundle);
            
            var locData = new List<LocationData>();
            
            locData.AddRange(item.Locations);
            
            foreach (var sku in compositionSkus)
            {
                returnable.CompositionSkus.Add(sku.ChildSku);
                
                var data = context.Whlnew.First(x => x.Sku == sku.ChildSku);
                var recursiveSearch = !context.SkuComposition.Any(x => x.BundleSku == sku.ChildSku.Substring(0, 7));
                
                var newItem = CreateSku(data,new List<WarehouseIssueLog>(), recursiveSearch).Result;
                
                foreach (var barcode in newItem.Barcodes)
                {
                    returnable.CompositionBarcodes.Add(barcode);
                }
                
                foreach (var keyword in newItem.SearchKeywords)
                {
                    returnable.CompositionSearchKeywords.Add(keyword);
                }
                
                locData.AddRange(newItem.Locations);
            }
            if (isNewSku)
            {
                item.Locations.Clear();
                item.Locations.AddRange(locData);
            }
            return returnable;
        }

        public static List<string> GetSkuBarcodes(this NewWhlSku item)
        {
            var hash = new HashSet<string>
            {
                item.Sku, 
                item.NewData.GS1Barcode
            };
            foreach (var supp in item.Suppliers)
            {
                hash.Add(supp.BarcodeDictionary["Item"]);
            }
            if (!item.ExtendedData.IsBundle) return hash.ToList();
            
            foreach (var compBarcode in item.BundleData.CompositionBarcodes)
            {
                hash.Add(compBarcode);
            }
            return hash.ToList();
        }

        public static HashSet<string> GetSearchKeywords(this NewWhlSku item)
        {
            var hash = new HashSet<string>
            {
                item.Sku, 
                item.NewData.GS1Barcode,
                item.ShortSku,
                item.ShortSku.Substring(2),
                item.Titles.Label,
                item.Titles.Invoice
            };
            foreach (var supp in item.Suppliers)
            {
                hash.Add(supp.SupplierCode);
                foreach (var value in supp.BarcodeDictionary.Values)
                {
                    hash.Add(value);
                }
            }
            foreach (var shelf in item.Locations)
            {
                hash.Add(shelf.ShelfName);
            }
            if (!item.ExtendedData.IsBundle) return hash;
            
            foreach (var compBarcode in item.BundleData.CompositionSearchKeywords)
            {
                hash.Add(compBarcode);
            }
            return hash;
        }

        public static void UpdateOrderwiseData(this NewWhlSku item, whldataContext context)
        {
            var owData = context.OrderwiseData.FirstOrDefault(x => x.Sku == item.Sku);
            if (owData == null) return;
            Enum.TryParse(owData.OwPostaltype, out item.NewData.NewPostalType);
            decimal.TryParse(owData.OwWeight.ToString(), out item.ItemData.Weight);
            bool.TryParse(owData.OwIsprepackfinalfinal, out item.NewData.IsPackdown);
            bool.TryParse(owData.Sys2IsPrepack.ToString(), out item.NewData.IsPrepack);
            item.NewData.PickingMaximum = 0;
            item.NewData.PickingMinimum = 0;
            item.RetailPrice = Convert.ToDecimal(owData.Guideprice);
            item.PriceData.Retail = Convert.ToDecimal(owData.Guideprice);
        }

        public static void GetMissedPicksAndIssues(this NewWhlSku item ,List<WarehouseIssueLog> issueLogs)
        {
            //var picks = context.Locationaudit.Count(x => x.AuditEvent == 4 && x.ShortSku == item.ShortSku && x.DateOfEvent >= DateTime.Now.AddDays(-56));
            item.NewData.PicksPerWeek = 0; //Convert.ToInt32(Math.Ceiling(0/8.0m));
            var issues = issueLogs.Where(x => x.Sku.Substring(0,7) == item.ShortSku && DateTime.Parse(x.DateReported) >= DateTime.Now.Date.AddDays(-56)).ToList();
            var misPick = new MispickStatistic
            {
                CantFinds = issues.Count(x => x.IssueType == "_CantFind"),
                Prepacks = issues.Count(x => x.IssueType =="_Prepack")
            };
            item.NewData.MissedPicks = misPick;
        }
    }
}