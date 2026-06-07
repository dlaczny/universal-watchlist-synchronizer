package com.watchlist.tv;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import java.util.Set;
import org.junit.Test;

public class BrowsingStateTest {
    @Test
    public void mediaConstants_matchUiAndApiValues() {
        assertEquals("all", BrowsingState.MEDIA_ALL);
        assertEquals("movie", BrowsingState.MEDIA_MOVIES);
        assertEquals("tv", BrowsingState.MEDIA_TV);
    }

    @Test
    public void defaults_startWithAllByDateAddedAndUnavailableItemsExcluded() {
        BrowsingState state = BrowsingState.defaults();

        assertEquals(BrowsingState.MEDIA_ALL, state.mediaType());
        assertEquals(CollectionOrganizer.SORT_DATE_ADDED, state.sortMode());
        assertFalse(state.includeUnavailable());
        assertNull(state.focusedItemId());
    }

    @Test
    public void withFocusedItemId_preservesFilters() {
        BrowsingState state = BrowsingState.defaults()
                .withMediaType(BrowsingState.MEDIA_TV)
                .withSortMode(CollectionOrganizer.SORT_ALPHABETICAL)
                .withIncludeUnavailable(true);

        BrowsingState updated = state.withFocusedItemId("tv-42");

        assertEquals(BrowsingState.MEDIA_TV, updated.mediaType());
        assertEquals(CollectionOrganizer.SORT_ALPHABETICAL, updated.sortMode());
        assertTrue(updated.includeUnavailable());
        assertEquals("tv-42", updated.focusedItemId());
    }

    @Test
    public void defaults_startWithPlexSelectedAndOtherServiceFiltersCleared() {
        BrowsingState state = BrowsingState.defaults();

        assertTrue(state.selectedAvailabilityServices().contains(BrowsingState.SERVICE_PLEX));
        assertEquals(1, state.selectedAvailabilityServices().size());
        assertFalse(state.isAvailabilityServiceSelected(BrowsingState.SERVICE_PRIME));
        assertFalse(state.includeUnavailable());
    }

    @Test
    public void withSelectedAvailabilityServices_copiesInputAndKeepsStateImmutable() {
        java.util.Set<String> selected = new java.util.LinkedHashSet<>();
        selected.add(BrowsingState.SERVICE_PRIME);
        selected.add(BrowsingState.SERVICE_HBO);

        BrowsingState original = BrowsingState.defaults();
        BrowsingState updated = original.withSelectedAvailabilityServices(selected);
        selected.add(BrowsingState.SERVICE_CRUNCHYROLL);

        assertTrue(updated.isAvailabilityServiceSelected(BrowsingState.SERVICE_PRIME));
        assertTrue(updated.isAvailabilityServiceSelected(BrowsingState.SERVICE_HBO));
        assertFalse(updated.isAvailabilityServiceSelected(BrowsingState.SERVICE_CRUNCHYROLL));
        assertTrue(original.isAvailabilityServiceSelected(BrowsingState.SERVICE_PLEX));
        assertFalse(original.isAvailabilityServiceSelected(BrowsingState.SERVICE_PRIME));
    }

    @Test
    public void withAvailabilityServiceSelection_togglesOneServiceWithoutChangingOthers() {
        BrowsingState state = BrowsingState.defaults()
                .withAvailabilityServiceSelection(BrowsingState.SERVICE_PRIME, true)
                .withAvailabilityServiceSelection(BrowsingState.SERVICE_PLEX, false);

        assertFalse(state.isAvailabilityServiceSelected(BrowsingState.SERVICE_PLEX));
        assertTrue(state.isAvailabilityServiceSelected(BrowsingState.SERVICE_PRIME));
        assertFalse(state.includeUnavailable());
    }

    @Test
    public void immutableUpdates_returnUpdatedStateWithoutChangingOriginal() {
        BrowsingState original = BrowsingState.defaults();

        BrowsingState mediaUpdated = original.withMediaType(BrowsingState.MEDIA_ALL);
        BrowsingState sortUpdated = original.withSortMode(CollectionOrganizer.SORT_ALPHABETICAL);
        BrowsingState availabilityUpdated = original.withIncludeUnavailable(true);
        BrowsingState focusUpdated = original.withFocusedItemId("movie-7");

        assertEquals(BrowsingState.MEDIA_ALL, original.mediaType());
        assertEquals(CollectionOrganizer.SORT_DATE_ADDED, original.sortMode());
        assertFalse(original.includeUnavailable());
        assertNull(original.focusedItemId());

        assertEquals(BrowsingState.MEDIA_ALL, mediaUpdated.mediaType());
        assertEquals(CollectionOrganizer.SORT_DATE_ADDED, mediaUpdated.sortMode());
        assertFalse(mediaUpdated.includeUnavailable());
        assertNull(mediaUpdated.focusedItemId());

        assertEquals(BrowsingState.MEDIA_ALL, sortUpdated.mediaType());
        assertEquals(CollectionOrganizer.SORT_ALPHABETICAL, sortUpdated.sortMode());
        assertFalse(sortUpdated.includeUnavailable());
        assertNull(sortUpdated.focusedItemId());

        assertEquals(BrowsingState.MEDIA_ALL, availabilityUpdated.mediaType());
        assertEquals(CollectionOrganizer.SORT_DATE_ADDED, availabilityUpdated.sortMode());
        assertTrue(availabilityUpdated.includeUnavailable());
        assertNull(availabilityUpdated.focusedItemId());

        assertEquals(BrowsingState.MEDIA_ALL, focusUpdated.mediaType());
        assertEquals(CollectionOrganizer.SORT_DATE_ADDED, focusUpdated.sortMode());
        assertFalse(focusUpdated.includeUnavailable());
        assertEquals("movie-7", focusUpdated.focusedItemId());
    }
}
