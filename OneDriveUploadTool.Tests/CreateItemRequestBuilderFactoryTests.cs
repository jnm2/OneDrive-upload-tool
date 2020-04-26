using Microsoft.Graph;
using NUnit.Framework;
using Shouldly;

namespace OneDriveUploadTool.Tests
{
    public static class CreateItemRequestBuilderFactoryTests
    {
        // https://docs.microsoft.com/en-us/onedrive/developer/rest-api/concepts/addressing-driveitems#path-encoding

        [Test]
        public static void Potential_URL_encoding_in_file_name_is_preserved()
        {
            var requestBuilder = new DriveItemRequestBuilder("https://placeholder:443/root", client: null);
            var factory = Program.CreateItemRequestBuilderFactory(requestBuilder, null);

            factory("FW%3asomething").RequestUrl.ShouldBe("https://placeholder:443/root:/FW%253asomething:");
        }

        [TestCase(@"Ryan's Files", "Ryan's%20Files")]
        [TestCase(@"Ryan's Files\doc (1).docx", "Ryan's%20Files/doc%20(1).docx")]
        [TestCase(@"Ryan's Files\estimate%s.docx", "Ryan's%20Files/estimate%25s.docx")]
        [TestCase(@"Break#Out", "Break%23Out")]
        [TestCase(@"Break#Out\saved_game[1].bin", "Break%23Out/saved_game[1].bin")]
        public static void Examples_from_docs(string path, string encoded)
        {
            var requestBuilder = new DriveItemRequestBuilder("https://placeholder:443/root", client: null);
            var factory = Program.CreateItemRequestBuilderFactory(requestBuilder, null);

            factory(path).RequestUrl.ShouldBe("https://placeholder:443/root:/" + encoded + ":");
        }
    }
}
