using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace StudentServicesMarketplace.Models
{
    public enum ServiceCategory
    {
        Tutoring, 
        Art, 
        Writing,
        [Display(Name = "Tech Support")] TechSupport, 
        Design, 
        Music,
        [Display(Name = "Photo Editing")] Photography,
        [Display(Name = "Video Editing")] videgraphy,
        [Display(Name = "Graphic Designing")] GraphicDesign,
        Other
    }

    public class Service
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [Required]
        [StringLength(2000)]
        public string Description { get; set; }

        [Required]
        public ServiceCategory Category { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [Range(0.01, 10000.00, ErrorMessage = "Price must be between $0.01 and $10,000.00")]
        public decimal Price { get; set; }

        public string? ImageUrl { get; set; } // Optional

        [Required]
        public string StudentId { get; set; } // FK to ApplicationUser (Student)
        [ForeignKey("StudentId")]
        public virtual ApplicationUser Student { get; set; }

        public DateTime DatePosted { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true; // Student can deactivate

        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}