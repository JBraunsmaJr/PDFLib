namespace PDFLib.Samples.WebApi.Data;

/// <summary>
/// Generates report data. Everything is derived from the id that is asked for, so the same request
/// always produces the same document and different ids produce different ones. That gives a load test
/// realistic variety without needing a database.
/// </summary>
public static class SampleData
{
    private static readonly Company Northwind = new(
        "Northwind Industrial Supply", "1200 Harbor Boulevard, Suite 400", "Baltimore, MD 21230", "United States",
        "US 52-1048873", "billing@northwind.example", "+1 (410) 555-0142");

    private static readonly Company Bank = new(
        "Harbor Federal Credit Union", "88 Light Street", "Baltimore, MD 21202", "United States",
        "Routing 052000113", "support@harborfcu.example", "+1 (410) 555-0199");

    private static readonly string[] CustomerNames =
    [
        "Aldergate Manufacturing", "Blue Ridge Fabrication", "Calloway & Finch Engineering", "Delmarva Marine Works",
        "Eastgate Logistics", "Fairmount Tool & Die", "Granite State Hydraulics", "Hollis Precision Parts",
        "Ironbridge Construction", "Juniper Valley Farms", "Keystone Rail Services", "Lakeshore Packaging"
    ];

    private static readonly string[] Contacts =
    [
        "Maria Okafor", "Daniel Whitlock", "Priya Raman", "Thomas Lindqvist", "Grace Nakamura", "Samuel Ortega",
        "Hannah Kowalski", "Victor Almeida"
    ];

    private static readonly string[] Cities =
    [
        "Richmond, VA 23219", "Pittsburgh, PA 15222", "Columbus, OH 43215", "Raleigh, NC 27601",
        "Hartford, CT 06103", "Albany, NY 12207", "Wilmington, DE 19801", "Charleston, WV 25301"
    ];

    private static readonly (string Sku, string Description, string Unit, decimal Price)[] Catalog =
    [
        ("HX-2210", "Hydraulic cylinder, double acting, 2\" bore x 10\" stroke", "ea", 284.50m),
        ("BR-6205", "Deep groove ball bearing 6205-2RS, sealed", "ea", 11.85m),
        ("VB-0750", "V-belt, A-section, 75\" outside length", "ea", 18.40m),
        ("FS-M12", "Hex cap screw M12 x 50, class 10.9, zinc plated", "box", 42.00m),
        ("GK-3300", "Gasket kit for series 3300 gear pump", "kit", 96.75m),
        ("LB-EP2", "Lithium complex grease EP2, 14 oz cartridge", "case", 78.20m),
        ("PT-0438", "Pneumatic tubing, polyurethane, 3/8\" OD, 100 ft", "roll", 64.90m),
        ("SW-PRX", "Inductive proximity sensor, M18, PNP, 8 mm range", "ea", 53.25m),
        ("MC-0500", "Motor contactor, 3 pole, 50 A, 24 VDC coil", "ea", 132.00m),
        ("FL-10M", "Hydraulic return filter element, 10 micron", "ea", 37.60m),
        ("CH-0080", "Roller chain ANSI 80, 10 ft box with connecting link", "box", 88.10m),
        ("SV-4W", "Directional control valve, 4-way, 3-position, D03", "ea", 219.00m),
        ("LAB-ON", "On-site installation and commissioning", "hr", 145.00m),
        ("FRT-LTL", "Palletised freight handling", "ea", 60.00m)
    ];

    private static T Pick<T>(Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    private static Party MakeParty(Random rng) =>
        new(Pick(rng, CustomerNames), Pick(rng, Contacts), $"{rng.Next(100, 9900)} {Pick(rng, ["Commerce Drive", "Industrial Parkway", "Foundry Road", "Mill Street", "Canal Avenue"])}",
            Pick(rng, Cities), "United States");

    // ------------------------------------------------------------------ invoice

    public static InvoiceModel Invoice(int number)
    {
        var rng = new Random(number);
        var billTo = MakeParty(rng);
        var shipTo = rng.Next(3) == 0 ? billTo with { Street = $"Dock {rng.Next(1, 9)}, {billTo.Street}" } : billTo;

        var lineCount = rng.Next(4, 15);
        var lines = Catalog
            .OrderBy(_ => rng.Next())
            .Take(lineCount)
            .Select(item => new InvoiceLine(
                item.Sku, item.Description,
                item.Unit == "hr" ? rng.Next(2, 17) : rng.Next(1, 41),
                item.Unit, item.Price,
                rng.Next(5) == 0 ? 0.10m : rng.Next(7) == 0 ? 0.05m : 0m))
            .ToList();

        var issued = new DateOnly(2026, 1, 1).AddDays(number % 300);

        return new InvoiceModel(
            number, issued, issued.AddDays(30), $"PO-{rng.Next(40000, 99999)}",
            Northwind, billTo, shipTo, lines,
            TaxRate: 0.06m,
            Shipping: Math.Round(25 + (decimal)rng.NextDouble() * 180, 2),
            Iban: "US64 HFCU 0520 0011 3004 7781 92",
            Notes: "Payment is due within 30 days of the invoice date. A late fee of 1.5% per month applies to overdue balances. " +
                   "Goods remain the property of Northwind Industrial Supply until paid in full.");
    }

    // ------------------------------------------------------------------ statement

    private static readonly (string Description, string Category, int Min, int Max)[] Spending =
    [
        ("Grocery Outlet", "Groceries", 18, 190), ("Harbor Market", "Groceries", 9, 120),
        ("Metro Transit Pass", "Transport", 2, 95), ("Shoreline Fuel", "Transport", 25, 80),
        ("Tidewater Electric", "Utilities", 60, 210), ("City Water Authority", "Utilities", 30, 90),
        ("Fiber Internet", "Utilities", 55, 85), ("Corner Bistro", "Dining", 12, 110),
        ("Coffee Roasters", "Dining", 3, 18), ("Northside Pharmacy", "Health", 8, 140),
        ("Fitness Collective", "Health", 35, 60), ("Streaming Plus", "Entertainment", 9, 22),
        ("Harbor Cinema", "Entertainment", 12, 45), ("Home Supply Depot", "Household", 15, 320),
        ("Online Marketplace", "Shopping", 10, 260)
    ];

    public static StatementModel Statement(int accountId, int months)
    {
        months = Math.Clamp(months, 1, 36);
        var rng = new Random(accountId);
        var holder = new Party(Pick(rng, Contacts), "", $"{rng.Next(10, 990)} {Pick(rng, ["Maple Lane", "Chestnut Street", "Bayview Terrace", "Orchard Court"])}",
            Pick(rng, Cities), "United States");

        var to = new DateOnly(2026, 10, 31);
        var from = to.AddMonths(-months).AddDays(1);
        var opening = Math.Round(1500 + (decimal)rng.NextDouble() * 6000, 2);
        var salary = Math.Round(3200 + (decimal)rng.NextDouble() * 2600, 2);
        var rent = Math.Round(1100 + (decimal)rng.NextDouble() * 900, 2);

        var transactions = new List<Transaction>();
        var balance = opening;
        var reference = 100000 + accountId % 50000;

        void Add(DateOnly date, string description, string category, decimal amount)
        {
            balance += amount;
            transactions.Add(new Transaction(date, $"{reference++:D6}", description, category, amount, balance));
        }

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.Day == 1) Add(day, "Rent payment - Bayview Property Management", "Housing", -rent);
            if (day.Day == 15 || day.Day == DateTime.DaysInMonth(day.Year, day.Month))
                Add(day, "Payroll deposit - " + Pick(rng, CustomerNames), "Income", salary / 2);
            if (day.Day == 20) Add(day, "Transfer to savings ••4471", "Savings", -Math.Round(salary * 0.1m, 2));

            for (var n = rng.Next(0, 4); n > 0; n--)
            {
                var item = Pick(rng, Spending);
                Add(day, item.Description, item.Category, -Math.Round(item.Min + (decimal)rng.NextDouble() * (item.Max - item.Min), 2));
            }

            if (rng.Next(45) == 0) Add(day, "Refund - Online Marketplace", "Shopping", Math.Round(10 + (decimal)rng.NextDouble() * 90, 2));
        }

        return new StatementModel(
            accountId, $"••••  ••••  {1000 + accountId % 9000}", Bank, holder, from, to, opening, transactions);
    }

    // ------------------------------------------------------------------ sales report

    public static SalesModel Sales(int year)
    {
        var rng = new Random(year);
        string[] monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

        var baseline = 380_000m + (year - 2020) * 45_000m;
        var months = monthNames.Select((name, i) =>
        {
            var seasonal = 1 + 0.18m * (decimal)Math.Sin((i - 2) / 12.0 * 2 * Math.PI);
            var revenue = Math.Round(baseline * seasonal * (0.9m + (decimal)rng.NextDouble() * 0.25m), 0);
            var target = Math.Round(baseline * seasonal * 1.03m, 0);
            return new MonthlySales(name, revenue, target, (int)(revenue / rng.Next(1400, 2300)));
        }).ToList();

        var total = months.Sum(m => m.Revenue);

        (string Name, decimal Share)[] regionShares =
            [("Mid-Atlantic", 0.31m), ("Northeast", 0.24m), ("Southeast", 0.19m), ("Midwest", 0.17m), ("West", 0.09m)];
        var regions = regionShares
            .Select(r => new RegionSales(r.Name, Math.Round(total * r.Share, 0),
                Math.Round(total * r.Share * (0.8m + (decimal)rng.NextDouble() * 0.3m), 0)))
            .ToList();

        (string Name, decimal Share, decimal Margin)[] productShares =
        [
            ("Hydraulics", 0.34m, 0.31m), ("Power transmission", 0.22m, 0.27m), ("Fasteners", 0.15m, 0.38m),
            ("Sensors & controls", 0.14m, 0.42m), ("Field services", 0.10m, 0.55m), ("Lubricants", 0.05m, 0.24m)
        ];
        var products = productShares
            .Select(p => new ProductSales(p.Name, Math.Round(total * p.Share, 0), (int)(total * p.Share / rng.Next(40, 260)), p.Margin))
            .ToList();

        var accounts = CustomerNames
            .Select(name => new AccountRow(
                name, Pick(rng, regionShares).Name, Pick(rng, Contacts), rng.Next(12, 140),
                Math.Round(total * (0.015m + (decimal)rng.NextDouble() * 0.06m), 0),
                Math.Round(-0.18m + (decimal)rng.NextDouble() * 0.5m, 3)))
            .OrderByDescending(a => a.Revenue)
            .ToList();

        return new SalesModel(year, Northwind, months, regions, products, accounts);
    }

    // ------------------------------------------------------------------ service agreement

    public static AgreementModel Agreement(string id)
    {
        var seed = id.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));
        var rng = new Random(seed);
        var customer = MakeParty(rng);
        var term = Pick(rng, new[] { 12, 24, 36 });

        var fees = new List<FeeLine>
        {
            new("Preventive maintenance programme (quarterly site visits)", "Monthly", Math.Round(1800 + (decimal)rng.NextDouble() * 2400, 0)),
            new("24/7 emergency call-out, 4 hour response", "Monthly", Math.Round(600 + (decimal)rng.NextDouble() * 900, 0)),
            new("Condition monitoring and reporting", "Monthly", Math.Round(350 + (decimal)rng.NextDouble() * 500, 0)),
            new("Initial equipment audit and asset register", "One-off", Math.Round(4500 + (decimal)rng.NextDouble() * 3000, 0))
        };

        var sections = new List<AgreementSection>
        {
            new("Scope of services",
            [
                "The Provider shall maintain the hydraulic, pneumatic and power transmission equipment listed in the asset register at the Customer's premises, in accordance with the manufacturer's recommendations and the service levels set out in this Agreement.",
                "Services comprise scheduled preventive maintenance, corrective repairs, emergency call-out and condition monitoring. Work outside this scope will be quoted separately and performed only on the Customer's written instruction."
            ]),
            new("Service levels",
            [
                "The Provider shall respond to emergency call-outs within four (4) hours of notification, twenty-four hours a day, and shall use reasonable endeavours to restore the affected equipment to operation within twenty-four (24) hours.",
                "Scheduled visits shall be agreed at least ten (10) business days in advance. If the Provider misses the emergency response target more than twice in a calendar quarter, the Customer is entitled to a service credit of five percent (5%) of that quarter's monthly fees."
            ]),
            new("Fees and payment",
            [
                "The Customer shall pay the fees set out in the schedule of fees. Monthly fees are invoiced in advance and one-off fees on completion. Invoices are payable within thirty (30) days of the invoice date.",
                "Fees are fixed for the first twelve (12) months. Thereafter the Provider may adjust fees once per year by no more than the change in the Consumer Price Index, on sixty (60) days' written notice."
            ]),
            new("Parts and materials",
            [
                "Consumables used during scheduled maintenance are included in the monthly fees. Replacement parts are charged at the Provider's list price less fifteen percent (15%) and carry a twelve (12) month warranty against defects in materials and workmanship."
            ]),
            new("Confidentiality",
            [
                "Each party shall keep confidential all information of a confidential nature obtained from the other party in connection with this Agreement, and shall use it only for the purposes of this Agreement. This obligation survives termination for a period of three (3) years."
            ]),
            new("Liability",
            [
                "Neither party excludes liability for death or personal injury caused by its negligence, or for fraud. Subject to that, each party's total liability under this Agreement in any twelve (12) month period is limited to the fees paid or payable in that period.",
                "Neither party shall be liable for loss of profit, loss of production or any indirect or consequential loss."
            ]),
            new("Term and termination",
            [
                $"This Agreement commences on the effective date and continues for an initial term of {term} months. It then renews for successive twelve (12) month periods unless either party gives ninety (90) days' written notice before the end of the current term.",
                "Either party may terminate this Agreement immediately by written notice if the other party commits a material breach and fails to remedy it within thirty (30) days of being asked to do so."
            ]),
            new("General",
            [
                "This Agreement is the entire agreement between the parties in relation to its subject matter. It may be varied only in writing signed by both parties. It is governed by the laws of the State of Maryland.",
                "This Agreement may be signed electronically and in counterparts. A digital signature applied to this document is as effective as a handwritten one."
            ])
        };

        return new AgreementModel(
            id, new DateOnly(2026, 12, 1), term, Northwind, "Eleanor Vance, Director of Field Services", customer, fees, sections);
    }
}
