package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import org.junit.Test;

public class CollectionOrganizerTest {
    @Test
    public void organize_whenUnavailableExcluded_returnsOnlyPlexAvailableItems() {
        List<WatchlistItem> items = Arrays.asList(
                item("movie-1", "First", "available_on_plex"),
                item("movie-2", "Second", "not_on_plex"),
                item("movie-3", "Third", "unknown_match"));

        List<WatchlistItem> result = CollectionOrganizer.organize(
                items, false, CollectionOrganizer.SORT_DATE_ADDED);

        assertIds(result, "movie-1");
    }

    @Test
    public void organize_whenUnavailableIncludedAndDateAdded_preservesBackendOrder() {
        List<WatchlistItem> items = Arrays.asList(
                item("movie-2", "Second", "not_on_plex"),
                item("movie-1", "First", "available_on_plex"),
                item("movie-3", "Third", "unknown_match"));

        List<WatchlistItem> result = CollectionOrganizer.organize(
                items, true, CollectionOrganizer.SORT_DATE_ADDED);

        assertIds(result, "movie-2", "movie-1", "movie-3");
    }

    @Test
    public void organize_whenAlphabetical_sortsTitlesIgnoringCase() {
        List<WatchlistItem> items = Arrays.asList(
                item("movie-3", "zodiac", "available_on_plex"),
                item("movie-1", "Alien", "available_on_plex"),
                item("movie-2", "blade runner", "available_on_plex"));

        List<WatchlistItem> result = CollectionOrganizer.organize(
                items, true, CollectionOrganizer.SORT_ALPHABETICAL);

        assertIds(result, "movie-1", "movie-2", "movie-3");
    }

    @Test
    public void organize_doesNotMutateInputList() {
        List<WatchlistItem> items = new ArrayList<>(Arrays.asList(
                item("movie-3", "zodiac", "available_on_plex"),
                item("movie-1", "Alien", "available_on_plex"),
                item("movie-2", "blade runner", "available_on_plex")));

        CollectionOrganizer.organize(items, true, CollectionOrganizer.SORT_ALPHABETICAL);

        assertIds(items, "movie-3", "movie-1", "movie-2");
    }

    private static WatchlistItem item(String id, String title, String availabilityStatus) {
        return new WatchlistItem(
                id,
                "movie",
                "letterboxd",
                id + "-source",
                title,
                2024,
                "Overview",
                null,
                null,
                "released",
                availabilityStatus,
                "2026-05-25T10:00:00+02:00");
    }

    private static void assertIds(List<WatchlistItem> items, String... ids) {
        assertEquals(ids.length, items.size());
        for (int index = 0; index < ids.length; index++) {
            assertEquals(ids[index], items.get(index).id());
        }
    }
}
