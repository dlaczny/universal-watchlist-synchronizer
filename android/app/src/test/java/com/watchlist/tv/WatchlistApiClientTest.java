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
                + "\"vodReleaseKnown\":true,"
                + "\"releasedOnVod\":true,"
                + "\"vodRegions\":[\"PL\",\"US\"],"
                + "\"ownedServiceAvailability\":[\"Amazon Prime Video\",\"Max\"],"
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
        assertEquals(true, item.vodReleaseKnown());
        assertEquals(true, item.releasedOnVod());
        assertEquals("PL", item.vodRegions().get(0));
        assertEquals("US", item.vodRegions().get(1));
        assertEquals("Amazon Prime Video", item.ownedServiceAvailability().get(0));
        assertEquals("Max", item.ownedServiceAvailability().get(1));
    }

    @Test
    public void parseItems_whenVodFieldsMissing_defaultsToNotReleasedOnVod() throws Exception {
        String json = "[{"
                + "\"id\":\"movie-future\","
                + "\"mediaType\":\"movie\","
                + "\"source\":\"letterboxd\","
                + "\"sourceId\":\"letterboxd-future\","
                + "\"title\":\"Future Movie\","
                + "\"year\":2026,"
                + "\"overview\":null,"
                + "\"posterUrl\":null,"
                + "\"backdropUrl\":null,"
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"not_on_plex\","
                + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\""
                + "}]";

        WatchlistItem item = WatchlistApiClient.parseItems(json).get(0);

        assertEquals(false, item.vodReleaseKnown());
        assertEquals(false, item.releasedOnVod());
        assertEquals(0, item.vodRegions().size());
        assertEquals(0, item.ownedServiceAvailability().size());
    }

    @Test
    public void formatAvailability_whenMovieIsUnavailableAndNotReleasedOnVod_returnsNotReleased() {
        WatchlistItem item = new WatchlistItem(
                "movie-future",
                "movie",
                "letterboxd",
                "letterboxd-future",
                "Future Movie",
                2026,
                null,
                null,
                null,
                "released",
                "not_on_plex",
                true,
                false,
                List.of(),
                List.of(),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");

        assertEquals("Not released", MainActivity.formatAvailability(item));
    }

    @Test
    public void formatAvailability_whenVodReleaseIsUnknown_returnsUnavailable() {
        WatchlistItem item = new WatchlistItem(
                "movie-unsynced",
                "movie",
                "letterboxd",
                "letterboxd-unsynced",
                "Unsynced Movie",
                2026,
                null,
                null,
                null,
                "released",
                "not_on_plex",
                false,
                false,
                List.of(),
                List.of(),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");

        assertEquals("Unavailable", MainActivity.formatAvailability(item));
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

    @Test
    public void buildAvailabilityRefreshPath_usesStartupRefreshEndpoint() {
        assertEquals("/api/sync/availability/refresh", WatchlistApiClient.buildAvailabilityRefreshPath());
    }

    @Test
    public void parseAvailabilityRefreshResult_parsesCompletedResponse() throws Exception {
        String json = "{"
                + "\"status\":\"completed\","
                + "\"ranPlexSync\":true,"
                + "\"reason\":\"stale\","
                + "\"startedAt\":\"2026-06-05T12:00:00Z\","
                + "\"finishedAt\":\"2026-06-05T12:00:05Z\","
                + "\"plex\":{\"watchlistItemsMatched\":40}"
                + "}";

        AvailabilityRefreshResult result = WatchlistApiClient.parseAvailabilityRefreshResult(json);

        assertEquals("completed", result.status());
        assertEquals(true, result.ranPlexSync());
        assertEquals("stale", result.reason());
    }

    @Test
    public void parseAvailabilityRefreshResult_parsesSkippedResponse() throws Exception {
        String json = "{"
                + "\"status\":\"skipped\","
                + "\"ranPlexSync\":false,"
                + "\"reason\":\"fresh\","
                + "\"startedAt\":\"2026-06-05T12:00:00Z\","
                + "\"finishedAt\":\"2026-06-05T12:00:00Z\","
                + "\"plex\":null"
                + "}";

        AvailabilityRefreshResult result = WatchlistApiClient.parseAvailabilityRefreshResult(json);

        assertEquals("skipped", result.status());
        assertEquals(false, result.ranPlexSync());
        assertEquals("fresh", result.reason());
    }

    @Test
    public void formatAvailability_whenMovieIsOnOwnedProvider_returnsProviderBadge() {
        WatchlistItem item = new WatchlistItem(
                "movie-prime",
                "movie",
                "letterboxd",
                "letterboxd-prime",
                "Prime Movie",
                2025,
                null,
                null,
                null,
                "released",
                "not_on_plex",
                true,
                true,
                List.of("PL"),
                List.of("Amazon Prime Video"),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");

        assertEquals("Prime", MainActivity.formatAvailability(item));
    }

    @Test
    public void formatAvailability_whenMovieIsOnPlexAndOwnedProvider_returnsPlexBadge() {
        WatchlistItem item = new WatchlistItem(
                "movie-plex",
                "movie",
                "letterboxd",
                "letterboxd-plex",
                "Plex Movie",
                2025,
                null,
                null,
                null,
                "released",
                "available_on_plex",
                true,
                true,
                List.of("PL"),
                List.of("Amazon Prime Video"),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");

        assertEquals("On Plex", MainActivity.formatAvailability(item));
    }

    @Test
    public void buildWatchlistDetailPath_usesItemId() {
        assertEquals("/api/watchlist/movie-dune-part-two", WatchlistApiClient.buildWatchlistDetailPath("movie-dune-part-two"));
    }

    @Test
    public void parseItemDetails_parsesRichDetailJson() throws Exception {
        String json = "{"
                + "\"id\":\"movie-dune-part-two\","
                + "\"mediaType\":\"movie\","
                + "\"source\":\"letterboxd\","
                + "\"sourceId\":\"letterboxd-dune-part-two\","
                + "\"title\":\"Dune: Part Two\","
                + "\"year\":2024,"
                + "\"overview\":\"Overview\","
                + "\"posterUrl\":\"/api/images/tmdb/w500/poster.jpg\","
                + "\"backdropUrl\":\"/api/images/tmdb/w1280/backdrop.jpg\","
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"available_on_plex\","
                + "\"vodReleaseKnown\":true,"
                + "\"releasedOnVod\":true,"
                + "\"vodRegions\":[\"PL\"],"
                + "\"ownedServiceAvailability\":[\"Amazon Prime Video\"],"
                + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\","
                + "\"genres\":[\"Science Fiction\",\"Adventure\"],"
                + "\"runtimeMinutes\":166,"
                + "\"originalLanguage\":\"en\","
                + "\"tmdbVoteAverage\":8.1,"
                + "\"tmdbVoteCount\":7000,"
                + "\"primaryActionLabel\":\"Open in Plex\","
                + "\"primaryActionEnabled\":true,"
                + "\"primaryActionTarget\":null"
                + "}";

        WatchlistItemDetails details = WatchlistApiClient.parseItemDetails(json, "http://10.0.2.2:5000");

        assertEquals("movie-dune-part-two", details.id());
        assertEquals("http://10.0.2.2:5000/api/images/tmdb/w500/poster.jpg", details.posterUrl());
        assertEquals(Integer.valueOf(166), details.runtimeMinutes());
        assertEquals("en", details.originalLanguage());
        assertEquals(Double.valueOf(8.1), details.tmdbVoteAverage());
        assertEquals(Integer.valueOf(7000), details.tmdbVoteCount());
        assertEquals("Science Fiction", details.genres().get(0));
        assertEquals("Open in Plex", details.primaryActionLabel());
        assertEquals(true, details.primaryActionEnabled());
        assertEquals(null, details.primaryActionTarget());
    }

    @Test
    public void parseItemDetails_whenOptionalFieldsMissing_defaultsSafely() throws Exception {
        String json = "{"
                + "\"id\":\"movie-minimal\","
                + "\"mediaType\":\"movie\","
                + "\"source\":\"letterboxd\","
                + "\"sourceId\":\"source\","
                + "\"title\":\"Minimal\","
                + "\"year\":null,"
                + "\"overview\":null,"
                + "\"posterUrl\":null,"
                + "\"backdropUrl\":null,"
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"not_on_plex\","
                + "\"vodReleaseKnown\":false,"
                + "\"releasedOnVod\":false,"
                + "\"vodRegions\":[],"
                + "\"ownedServiceAvailability\":[],"
                + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\","
                + "\"primaryActionLabel\":\"Unavailable\","
                + "\"primaryActionEnabled\":false,"
                + "\"primaryActionTarget\":null"
                + "}";

        WatchlistItemDetails details = WatchlistApiClient.parseItemDetails(json);

        assertEquals(0, details.genres().size());
        assertEquals(null, details.runtimeMinutes());
        assertEquals(null, details.originalLanguage());
        assertEquals(null, details.tmdbVoteAverage());
        assertEquals(null, details.tmdbVoteCount());
    }

    @Test
    public void formatAvailability_whenMovieHasMultipleOwnedProviders_returnsCompactProviderBadge() {
        WatchlistItem item = new WatchlistItem(
                "movie-multi",
                "movie",
                "letterboxd",
                "letterboxd-multi",
                "Multi Provider Movie",
                2025,
                null,
                null,
                null,
                "released",
                "not_on_plex",
                true,
                true,
                List.of("PL"),
                List.of("HBO Max", "Amazon Prime Video"),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");

        assertEquals("Max +1", MainActivity.formatAvailability(item));
    }
}
