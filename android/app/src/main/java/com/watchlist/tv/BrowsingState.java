package com.watchlist.tv;

public final class BrowsingState {
    public static final String MEDIA_ALL = "all";
    public static final String MEDIA_MOVIES = WatchlistFilters.MEDIA_MOVIE;
    public static final String MEDIA_TV = WatchlistFilters.MEDIA_TV;

    private final String mediaType;
    private final String sortMode;
    private final boolean includeUnavailable;
    private final String focusedItemId;

    private BrowsingState(
            String mediaType,
            String sortMode,
            boolean includeUnavailable,
            String focusedItemId) {
        this.mediaType = mediaType;
        this.sortMode = sortMode;
        this.includeUnavailable = includeUnavailable;
        this.focusedItemId = focusedItemId;
    }

    public static BrowsingState defaults() {
        return new BrowsingState(MEDIA_ALL, CollectionOrganizer.SORT_DATE_ADDED, false, null);
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

    public BrowsingState withMediaType(String mediaType) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, focusedItemId);
    }

    public BrowsingState withSortMode(String sortMode) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, focusedItemId);
    }

    public BrowsingState withIncludeUnavailable(boolean includeUnavailable) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, focusedItemId);
    }

    public BrowsingState withFocusedItemId(String focusedItemId) {
        return new BrowsingState(mediaType, sortMode, includeUnavailable, focusedItemId);
    }
}
