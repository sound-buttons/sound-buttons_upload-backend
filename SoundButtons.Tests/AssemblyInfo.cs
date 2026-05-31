using Xunit;

// Several tests mutate process-global state (environment variables, PATH, and the
// current working directory) to exercise binary discovery and configuration code
// paths. Disable cross-class parallelization so those mutations cannot race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
