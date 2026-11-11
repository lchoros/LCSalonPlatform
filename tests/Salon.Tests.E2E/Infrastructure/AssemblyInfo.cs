// Every E2E class hosts its own Kestrel against the one shared test database and seeds the same demo slugs, so two
// classes running at once can replace each other's rows mid-navigation (seen as 30 s Playwright timeouts on CI).
// Run the collections one after another; the suite is small and each class still parallelises nothing internally.
[assembly: CollectionBehavior(DisableTestParallelization = true)]