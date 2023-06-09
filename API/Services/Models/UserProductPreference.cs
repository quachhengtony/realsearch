namespace Realsearch.API.Models;

class UserProductPreference
{
    public string BuyerEmail { get; set; }
    public string ProductColor { get; set; }
    public string ProductGender { get; set; }
    public string ProductUsage { get; set; }
    public string ProductSeason { get; set; }
    public float[] ProductTextVector { get; set; }
}