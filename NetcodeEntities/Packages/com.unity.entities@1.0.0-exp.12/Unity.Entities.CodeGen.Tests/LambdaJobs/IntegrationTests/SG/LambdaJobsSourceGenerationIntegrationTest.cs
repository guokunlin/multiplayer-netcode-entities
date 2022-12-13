using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public abstract class LambdaJobsSourceGenerationIntegrationTest : IntegrationTest
    {
        protected override string ExpectedPath =>
            "Packages/com.unity.entities/Unity.Entities.CodeGen.Tests/LambdaJobs/IntegrationTests/SG";

        protected void RunTest(string cSharpCode, params GeneratedType[] generatedTypes)
        {
            var (isSuccess, compilerMessages) = TestCompiler.Compile(cSharpCode, new[]
            {
                typeof(Unity.Entities.SystemBase),
                typeof(Unity.Jobs.JobHandle),
                typeof(Unity.Burst.BurstCompileAttribute),
                typeof(Unity.Mathematics.float3),
                typeof(Unity.Collections.ReadOnlyAttribute),  // This now lives in UnityEngine.CoreModule.dll instead of Unity.Collections.dll
                typeof(Unity.Collections.RewindableAllocator),
                typeof(Unity.Entities.CodeGen.Tests.Translation),
                typeof(Unity.Entities.Tests.EcsTestData),
                typeof(Unity.Entities.CodeGen.Tests.TestTypes.TranslationInAnotherAssembly)
            }, true);

            if (!isSuccess)
                Assert.Fail($"Compilation failed with errors {string.Join(Environment.NewLine, compilerMessages.Select(msg => msg.message))}");

            RunSourceGenerationTest(generatedTypes, Path.Combine(TestCompiler.DirectoryForTestDll, TestCompiler.OutputDllName));
            TestCompiler.CleanUp();
        }
    }
}
