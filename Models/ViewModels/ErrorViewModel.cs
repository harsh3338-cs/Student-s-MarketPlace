﻿namespace StudentServicesMarketplace.Models.ViewModels
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool showRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
