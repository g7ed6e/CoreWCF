// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using CoreWCF.DataContractSerialization.TestCorpus;
using CoreWCF.DataContractSerialization.Tests.Harness;
using Xunit;

namespace CoreWCF.DataContractSerialization.Tests
{
    /// <summary>
    /// Keeps the corpus, the catalog and the fixtures on disk in agreement.
    /// </summary>
    /// <remarks>
    /// Reflection is fine here - the tests need not be ahead-of-time safe, only the corpus does.
    /// </remarks>
    public class CorpusIntegrityTests
    {
        private static IEnumerable<Type> DataContractTypes()
        {
            Assembly corpus = typeof(CorpusCatalog).Assembly;
            foreach (Type type in corpus.GetTypes())
            {
                // IsPublic is false for nested types however visible they are, so a public nested
                // contract would otherwise slip through this check unnoticed.
                if (!(type.IsPublic || type.IsNestedPublic) || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (type.GetCustomAttribute<DataContractAttribute>(inherit: false) != null)
                {
                    yield return type;
                }
            }
        }

        [Fact]
        public void EveryDataContractTypeIsRegisteredOrExplicitlySkipped()
        {
            HashSet<Type> covered = new HashSet<Type>(CorpusCatalog.Cases.Select(c => c.ContractType));
            HashSet<Type> excluded = new HashSet<Type>(CorpusCatalog.Exclusions.Select(e => e.ContractType));

            List<string> unaccounted = DataContractTypes()
                .Where(t => !covered.Contains(t) && !excluded.Contains(t))
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                unaccounted.Count == 0,
                "Every [DataContract] type in the corpus must either have a registered case or an explicit " +
                "builder.Skip<T>(reason). Unaccounted for:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", unaccounted));
        }

        [Fact]
        public void CaseIdsAreUnique()
        {
            List<string> duplicates = CorpusCatalog.Cases
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, "Duplicate case ids: " + string.Join(", ", duplicates));
        }

        [Fact]
        public void FixtureFileNamesAreUniqueIgnoringCase()
        {
            // Windows file systems are case-insensitive, so two ids differing only in case would
            // silently collapse onto one fixture and each would overwrite the other.
            List<string> duplicates = CorpusCatalog.Cases
                .GroupBy(c => c.FixtureFileName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + " <- " + string.Join(", ", g.Select(c => c.Id)))
                .ToList();

            Assert.True(duplicates.Count == 0, "Fixture file name collisions:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
        }

        [Fact]
        public void EveryCaseHasAFixture()
        {
            List<string> missing = new List<string>();

            foreach (CorpusCase corpusCase in CorpusCatalog.Cases)
            {
                byte[] ignored;
                string resolvedPath;
                if (!FixtureStore.TryRead(corpusCase.FixtureFileName, out ignored, out resolvedPath))
                {
                    missing.Add(corpusCase.Id);
                }
            }

            Assert.True(
                missing.Count == 0,
                "Cases without a golden fixture (regenerate with " + FixtureStore.RegenerateEnvironmentVariable + "=1):" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        [Fact]
        public void NoOrphanFixtures()
        {
            // Catches fixture rot: a renamed or deleted case leaves a file recording nothing.
            //
            // Only meaningful on the baseline framework. Some cases are compiled conditionally -
            // DateTimeOnlyWrapper needs DateOnly/TimeOnly, which .NET Framework lacks - so on any
            // other framework the catalog is legitimately a subset of the fixtures on disk.
            if (!TargetFrameworkInfo.IsBaseline)
            {
                Assert.Skip(
                    "Orphan detection runs on " + TargetFrameworkInfo.BaselineTargetFramework +
                    ", where the catalog is complete; elsewhere conditionally-compiled cases make it a subset.");
            }

            if (!Directory.Exists(FixtureStore.OutputFixtureDirectory))
            {
                return;
            }

            HashSet<string> expected = new HashSet<string>(
                CorpusCatalog.Cases.Select(c => c.FixtureFileName),
                StringComparer.OrdinalIgnoreCase);

            List<string> orphans = Directory
                .GetFiles(FixtureStore.OutputFixtureDirectory, "*" + FixtureNaming.Extension, SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(name => !expected.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                orphans.Count == 0,
                "Fixture files with no corresponding corpus case:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", orphans));
        }

        [Fact]
        public void EveryExclusionStatesAReason()
        {
            foreach (CorpusExclusion exclusion in CorpusCatalog.Exclusions)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(exclusion.Reason),
                    "Exclusion of " + exclusion.ContractType.FullName + " must state why.");
            }
        }

        [Fact]
        public void CorpusDoesNotReferenceCoreWcf()
        {
            // The corpus must stay pure BCL so it remains publishable ahead-of-time as a smoke
            // application, and so the generator's input is free of CoreWCF types.
            List<string> coreWcfReferences = typeof(CorpusCatalog).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("CoreWCF", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                coreWcfReferences.Count == 0,
                "The test corpus must not reference CoreWCF. Found: " + string.Join(", ", coreWcfReferences));
        }
    }
}
