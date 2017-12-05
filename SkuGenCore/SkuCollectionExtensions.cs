using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhiteHingeFramework.Classes.Items;

namespace SkuGenCore
{
    public static class SkuCollectionExtensions
    {
        public static List<NewWhlSku> SearchBarcodes(this List<NewWhlSku> collection,string searchTerm)
        {
            var returnable = new List<NewWhlSku>();
            if (searchTerm == "") return returnable;
            returnable.AddRange(collection.Where(x => x.Barcodes.Any(y => y == searchTerm)));
            return returnable;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="searchTerm"></param>
        /// <param name="compareOption">Where None is specified, the search will be case sensitive and search for containing, else exact matches only</param>
        /// <returns></returns>
        public static List<NewWhlSku> SearchKeywords(this List<NewWhlSku> collection, string searchTerm,CompareOptions compareOption = CompareOptions.None)
        {
            var returnable = new List<NewWhlSku>();
            
            if (searchTerm == "") return collection;
            if (compareOption != CompareOptions.None)
            {
                returnable.AddRange(collection.Where(x => x.SearchKeywords.Any(y => y.Contains(searchTerm))));
            }
            else
            {
                var compare = CultureInfo.InvariantCulture.CompareInfo.Compare("","",compareOption);
                returnable.AddRange(collection.Where(x => x.SearchKeywords.Any(y => CultureInfo.InvariantCulture.CompareInfo.Compare(y,searchTerm,compareOption) == 0 )));
            }
            return returnable;
        }
    }
}