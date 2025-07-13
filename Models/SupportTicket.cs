using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentServicesMarketplace.Models
{
    public enum TicketStatus
    {
        Open, InProgress, Resolved, Closed
    }

    public class SupportTicket
    {
        public int Id { get; set; }
        [Required] public string UserId { get; set; }
        [ForeignKey("UserId")] public virtual ApplicationUser User { get; set; }
        [Required, StringLength(200)] public string Subject { get; set; }
        [Required, StringLength(2000)] public string Message { get; set; }
        public TicketStatus Status { get; set; } = TicketStatus.Open;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedDate { get; set; }
        [StringLength(2000)] public string? AdminReply { get; set; }
        public string? AdminRepliedById { get; set; }
        [ForeignKey("AdminRepliedById")] public virtual ApplicationUser? AdminRepliedBy { get; set; }
        public DateTime? ReplyDate { get; set; }
    }
}