package com.watchlist.tv;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class WatchlistItem {
    private final String id;
    private final String mediaType;
    private final String source;
    private final String sourceId;
    private final String title;
    private final Integer year;
    private final String overview;
    private final String posterUrl;
    private final String backdropUrl;
    private final String releaseStatus;
    private final String availabilityStatus;
    private final boolean vodReleaseKnown;
    private final boolean releasedOnVod;
    private final List<String> vodRegions;
    private final List<String> ownedServiceAvailability;
    private final String addedAt;
    private final String updatedAt;

    public WatchlistItem(
            String id,
            String mediaType,
            String source,
            String sourceId,
            String title,
            Integer year,
            String overview,
            String posterUrl,
            String backdropUrl,
            String releaseStatus,
            String availabilityStatus,
            String addedAt,
            String updatedAt) {
        this(
                id,
                mediaType,
                source,
                sourceId,
                title,
                year,
                overview,
                posterUrl,
                backdropUrl,
                releaseStatus,
                availabilityStatus,
                false,
                false,
                List.of(),
                List.of(),
                addedAt,
                updatedAt);
    }

    public WatchlistItem(
            String id,
            String mediaType,
            String source,
            String sourceId,
            String title,
            Integer year,
            String overview,
            String posterUrl,
            String backdropUrl,
            String releaseStatus,
            String availabilityStatus,
            boolean vodReleaseKnown,
            boolean releasedOnVod,
            List<String> vodRegions,
            List<String> ownedServiceAvailability,
            String addedAt,
            String updatedAt) {
        this.id = id;
        this.mediaType = mediaType;
        this.source = source;
        this.sourceId = sourceId;
        this.title = title;
        this.year = year;
        this.overview = overview;
        this.posterUrl = posterUrl;
        this.backdropUrl = backdropUrl;
        this.releaseStatus = releaseStatus;
        this.availabilityStatus = availabilityStatus;
        this.vodReleaseKnown = vodReleaseKnown;
        this.releasedOnVod = releasedOnVod;
        this.vodRegions = Collections.unmodifiableList(new ArrayList<>(vodRegions));
        this.ownedServiceAvailability = Collections.unmodifiableList(new ArrayList<>(ownedServiceAvailability));
        this.addedAt = addedAt;
        this.updatedAt = updatedAt;
    }

    public String id() {
        return id;
    }

    public String mediaType() {
        return mediaType;
    }

    public String source() {
        return source;
    }

    public String sourceId() {
        return sourceId;
    }

    public String title() {
        return title;
    }

    public Integer year() {
        return year;
    }

    public String overview() {
        return overview;
    }

    public String posterUrl() {
        return posterUrl;
    }

    public String backdropUrl() {
        return backdropUrl;
    }

    public String releaseStatus() {
        return releaseStatus;
    }

    public String availabilityStatus() {
        return availabilityStatus;
    }

    public boolean vodReleaseKnown() {
        return vodReleaseKnown;
    }

    public boolean releasedOnVod() {
        return releasedOnVod;
    }

    public List<String> vodRegions() {
        return vodRegions;
    }

    public List<String> ownedServiceAvailability() {
        return ownedServiceAvailability;
    }

    public String addedAt() {
        return addedAt;
    }

    public String updatedAt() {
        return updatedAt;
    }
}
