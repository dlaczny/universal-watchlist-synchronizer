package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.List;
import org.junit.Test;

public class WatchlistApiClientTest {
    @Test
    public void buildWatchlistPath_whenAllPlexOnlyDateAdded_usesCollectionApiContract() {
        String path = WatchlistApiClient.buildWatchlistPath(
                BrowsingState.MEDIA_ALL,
                CollectionOrganizer.SORT_DATE_ADDED,
                false);

        assertEquals("/api/watchlist?collection=all&availability=plex&sort=added_desc", path);
    }

    @Test
    public void buildWatchlistPath_whenUnavailableIncluded_expandsSplitAvailabilityReasons() {
        String path = WatchlistApiClient.buildWatchlistPath(
                BrowsingState.MEDIA_TV,
                CollectionOrganizer.SORT_ALPHABETICAL,
                true);

        assertEquals(
                "/api/watchlist?collection=tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=title_asc",
                path);
    }

    @Test
    public void parseItems_parsesBackendWatchlistJson() throws Exception {
        String json = "[{"
                + "\"id\":\"movie-dune-part-two\","
                + "\"mediaType\":\"movie\","
                + "\"source\":\"letterboxd\","
                + "\"sourceId\":\"letterboxd-dune-part-two\","
                + "\"title\":\"Dune: Part Two\","
                + "\"year\":2024,"
                + "\"overview\":\"Overview\","
                + "\"posterUrl\":\"https://image.example/poster.jpg\","
                + "\"backdropUrl\":\"https://image.example/backdrop.jpg\","
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"available_on_plex\","
                + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\""
                + "}]";

        List<WatchlistItem> items = WatchlistApiClient.parseItems(json);

        assertEquals(1, items.size());
        WatchlistItem item = items.get(0);
        assertEquals("movie-dune-part-two", item.id());
        assertEquals("movie", item.mediaType());
        assertEquals("Dune: Part Two", item.title());
        assertEquals("available_on_plex", item.availabilityStatus());
    }

    @Test
    public void parseItems_parsesAddedAtAndUpdatedAt() throws Exception {
        String json = "[{"
                + "\"id\":\"tv-severance\","
                + "\"mediaType\":\"tv\","
                + "\"source\":\"tmdb\","
                + "\"sourceId\":\"tmdb-95396\","
                + "\"title\":\"Severance\","
                + "\"year\":2022,"
                + "\"overview\":\"Overview\","
                + "\"posterUrl\":null,"
                + "\"backdropUrl\":null,"
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"not_on_plex\","
                + "\"addedAt\":\"2026-05-23T09:30:00+02:00\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\""
                + "}]";

        WatchlistItem item = WatchlistApiClient.parseItems(json).get(0);

        assertEquals("2026-05-23T09:30:00+02:00", item.addedAt());
        assertEquals("2026-05-25T10:00:00+02:00", item.updatedAt());
    }

    @Test
    public void resolveImageUrl_whenBackendReturnsRelativePath_usesBackendBaseUrl() {
        String imageUrl = WatchlistApiClient.resolveImageUrl(
                "http://10.0.2.2:5000/",
                "/api/images/tmdb/w500/poster.jpg");

        assertEquals("http://10.0.2.2:5000/api/images/tmdb/w500/poster.jpg", imageUrl);
    }

    @Test
    public void resolveImageUrl_whenBackendReturnsAbsolutePath_keepsOriginalUrl() {
        String imageUrl = WatchlistApiClient.resolveImageUrl(
                "http://10.0.2.2:5000",
                "https://image.example/poster.jpg");

        assertEquals("https://image.example/poster.jpg", imageUrl);
    }

    @Test
    public void parseSyncStatus_parsesSeededStatus() throws Exception {
        String json = "{"
                + "\"status\":\"seeded\","
                + "\"lastSuccessfulSyncAt\":\"2026-05-25T10:00:00+02:00\""
                + "}";

        SyncStatus status = WatchlistApiClient.parseSyncStatus(json);

        assertEquals("seeded", status.status());
        assertEquals("2026-05-25T10:00:00+02:00", status.lastSuccessfulSyncAt());
    }
}
