namespace PDFLib.Samples.WebApi.Data;

public sealed record Company(string Name, string Street, string City, string Country, string TaxId, string Email, string Phone);

public sealed record Party(string Name, string Contact, string Street, string City, string Country);

// ---- Invoice

public sealed record InvoiceLine(string Sku, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountRate)
{
    public decimal Net => Math.Round(Quantity * UnitPrice * (1 - DiscountRate), 2);
}

public sealed record InvoiceModel(
    int Number,
    DateOnly Issued,
    DateOnly Due,
    string PurchaseOrder,
    Company Seller,
    Party BillTo,
    Party ShipTo,
    IReadOnlyList<InvoiceLine> Lines,
    decimal TaxRate,
    decimal Shipping,
    string Iban,
    string Notes)
{
    public decimal Subtotal => Lines.Sum(l => l.Net);
    public decimal Tax => Math.Round(Subtotal * TaxRate, 2);
    public decimal Total => Subtotal + Tax + Shipping;
}

// ---- Account statement

public sealed record Transaction(DateOnly Date, string Reference, string Description, string Category, decimal Amount, decimal Balance);

public sealed record StatementModel(
    int AccountId,
    string AccountNumber,
    Company Bank,
    Party Holder,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<Transaction> Transactions)
{
    public decimal ClosingBalance => Transactions.Count > 0 ? Transactions[^1].Balance : OpeningBalance;
    public decimal MoneyIn => Transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
    public decimal MoneyOut => -Transactions.Where(t => t.Amount < 0).Sum(t => t.Amount);

    public IReadOnlyList<(string Category, decimal Spent)> SpendingByCategory =>
        Transactions.Where(t => t.Amount < 0)
            .GroupBy(t => t.Category)
            .Select(g => (g.Key, -g.Sum(t => t.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
}

// ---- Sales report

public sealed record MonthlySales(string Month, decimal Revenue, decimal Target, int Orders);

public sealed record RegionSales(string Region, decimal Revenue, decimal PreviousYear);

public sealed record ProductSales(string Product, decimal Revenue, int Units, decimal Margin);

public sealed record AccountRow(string Customer, string Region, string Owner, int Orders, decimal Revenue, decimal Change);

public sealed record SalesModel(
    int Year,
    Company Company,
    IReadOnlyList<MonthlySales> Months,
    IReadOnlyList<RegionSales> Regions,
    IReadOnlyList<ProductSales> Products,
    IReadOnlyList<AccountRow> TopAccounts)
{
    public decimal Revenue => Months.Sum(m => m.Revenue);
    public decimal Target => Months.Sum(m => m.Target);
    public int Orders => Months.Sum(m => m.Orders);
    public decimal PreviousYearRevenue => Regions.Sum(r => r.PreviousYear);
    public decimal Growth => PreviousYearRevenue == 0 ? 0 : (Revenue - PreviousYearRevenue) / PreviousYearRevenue;
    public decimal AverageOrder => Orders == 0 ? 0 : Revenue / Orders;
    public decimal Attainment => Target == 0 ? 0 : Revenue / Target;
}

// ---- Service agreement

public sealed record AgreementSection(string Title, IReadOnlyList<string> Paragraphs);

public sealed record FeeLine(string Service, string Billing, decimal Amount);

public sealed record AgreementModel(
    string Id,
    DateOnly Effective,
    int TermMonths,
    Company Provider,
    string ProviderSignatory,
    Party Customer,
    IReadOnlyList<FeeLine> Fees,
    IReadOnlyList<AgreementSection> Sections);
