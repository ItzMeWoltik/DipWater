using System.Collections.Generic;

namespace InternetSupportBot
{
    public class UserData
    {
        public string? ContractNumber { get; set; }
        public string? Address { get; set; }
        public bool IsIdentified { get; set; }
        public string? TicketId { get; set; }
        public string? ProblemType { get; set; }
        public string? ProblemDetails { get; set; }
        public HashSet<string> AttemptedSolutions { get; set; } = new HashSet<string>();
        public int? MessageIdToEdit { get; set; }
    }
}