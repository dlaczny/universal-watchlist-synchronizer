package com.watchlist.tv;

import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

public final class BrowsingState {
    public static final String MEDIA_ALL = "all";
    public static final String MEDIA_MOVIES = WatchlistFilters.MEDIA_MOVIE;
    public static final String MEDIA_TV = WatchlistFilters.MEDIA_TV;

    public static final String SERVICE_PLEX = "plex";
    public static final String SERVICE_PRIME = "prime";
    public static final String SERVICE_HBO = "hbo";
    public static final String SERVICE_SKYSHOWTIME = "skyshowtime";
    public static final String SERVICE_CRUNCHYROLL = "crunchyroll";

    private final String mediaType;
    private final String sortMode;
    private final boolean includeUnavailable;
    private final Set<String> selectedAvailabilityServices;
    private final String focusedItemId;

    private BrowsingState(
            String mediaType,
            String sortMode,
            boolean includeUnavailable,
            Set<String> selectedAvailabilityServices,
            String focusedItemId) {
        this.mediaType = mediaType;
        this.sortMode = sortMode;
        this.includeUnavailable = includeUnavailable;
        this.selectedAvailabilityServices = Collections.unmodifiableSet(new LinkedHashSet<>(selectedAvailabilityServices));
        this.focusedItemId = focusedItemId;
    }

    public static BrowsingState defaults() {
        Set<String> selectedServices = new LinkedHashSet<>();
        selectedServices.add(SERVICE_PLEX);
        return new BrowsingState(
                MEDIA_ALL,
                CollectionOrganizer.SORT_DATE_ADDED,
                false,
                selectedServices,
                null);
    }

    public String mediaType() {
        return mediaType;
    }

    public String sortMode() {
        return sortMode;
    }

    public boolean includeUnavailable() {
        return includeUnavailable;
    }

    public String focusedItemId() {
        return focusedItemId;
    }

    public Set<String> selectedAvailabilityServices() {
        return selectedAvailabilityServices;
    }

    public boolean isAvailabilityServiceSelected(String service) {
        return selectedAvailabilityServices.contains(service);
    }

    public BrowsingState withMediaType(String mediaType) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, selectedAvailabilityServices, focusedItemId);
    }

    public BrowsingState withSortMode(String sortMode) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, selectedAvailabilityServices, focusedItemId);
    }

    public BrowsingState withIncludeUnavailable(boolean includeUnavailable) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, selectedAvailabilityServices, focusedItemId);
    }

    public BrowsingState withFocusedItemId(String focusedItemId) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, selectedAvailabilityServices, focusedItemId);
    }

    public BrowsingState withSelectedAvailabilityServices(Set<String> selectedAvailabilityServices) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, selectedAvailabilityServices, focusedItemId);
    }

    public BrowsingState withAvailabilityServiceSelection(String service, boolean selected) {
        Set<String> updated = new LinkedHashSet<>(selectedAvailabilityServices);
        if (selected) {
            updated.add(service);
        } else {
            updated.remove(service);
        }
        return withSelectedAvailabilityServices(updated);
    }
}
