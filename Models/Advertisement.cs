using System.ComponentModel.DataAnnotations;

namespace StudentServicesMarketplace.Models
{
    public enum AdPlacement
    {
        Sidebar, Header, Footer, Inline
    }

    public class Advertisement
    {
        public int Id { get; set; }
        [Required, StringLength(100)] public string Title { get; set; }
        [Required, Url] public string ImageUrl { get; set; }
        [Required, Url] public string TargetUrl { get; set; }
        public AdPlacement Placement { get; set; } = AdPlacement.Sidebar;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 0;
    }
}