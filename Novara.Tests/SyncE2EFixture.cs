using System.Text;
using System.Text.Json;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncE2EFixture
{


    private const string SpaceKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string LockPassword = "novara-test-123";





    private const string PlainJson =
        "{\"k\":\"v\",\"memoEntries\":[" +
        "{\"id\":\"m-live\",\"name\":\"live-entry\",\"type\":\"自定义\",\"fields\":[]}," +
        "{\"id\":\"m-dead\",\"name\":\"tombstone-entry\",\"type\":\"自定义\",\"fields\":[]," +
        "\"isDeleted\":true,\"deletedAt\":\"2026-09-14T00:00:00Z\"}]," +
        "\"diaryItems\":[" +
        "{\"id\":\"d-md\",\"title\":\"md-doc\",\"content\":\"# hello\",\"format\":\"markdown\"," +
        "\"createdAt\":\"2026-09-14T00:00:00Z\",\"modifiedAt\":\"2026-09-14T00:00:00Z\"}," +
        "{\"id\":\"d-html\",\"title\":\"html-doc\",\"content\":\"<p>rich</p>\",\"format\":\"html\"," +
        "\"createdAt\":\"2026-09-14T00:00:00Z\",\"modifiedAt\":\"2026-09-14T00:00:00Z\"}]}";






    [Fact]
    public void ProduceFixture()
    {
        var keywrapJson = SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(SpaceKey, LockPassword));
        var container = SyncContainer.Seal(
            Encoding.UTF8.GetBytes(PlainJson), SpaceKey, SyncContainer.NewVersionSalt());




        Assert.Equal(PlainJson, Encoding.UTF8.GetString(SyncContainer.Open(container, SpaceKey)));
        Assert.Equal(SpaceKey, SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(keywrapJson), LockPassword));
        Assert.Equal(4, container[4]);



        var json =
            "{\n" +
            "  \"spaceKey\": " + JsonSerializer.Serialize(SpaceKey) + ",\n" +
            "  \"keywrapJson\": " + JsonSerializer.Serialize(keywrapJson) + ",\n" +
            "  \"password\": " + JsonSerializer.Serialize(LockPassword) + ",\n" +
            "  \"containerBase64\": " + JsonSerializer.Serialize(Convert.ToBase64String(container)) + ",\n" +
            "  \"plainJson\": " + JsonSerializer.Serialize(PlainJson) + "\n" +
            "}\n";

        var path = Path.Combine(Path.GetTempPath(), "novara-m3-fixture.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        Assert.True(File.Exists(path));
    }
}
