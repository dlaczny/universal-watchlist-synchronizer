package com.watchlist.tv;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

public final class MainActivity extends Activity {
    private static final String PREFS_NAME = "browsing_state";
    private static final String PREF_MEDIA_TYPE = "media_type";
    private static final String PREF_SORT_MODE = "sort_mode";
    private static final String PREF_INCLUDE_UNAVAILABLE = "include_unavailable";
    private static final String PREF_FOCUSED_ITEM_ID = "focused_item_id";
    private static final String PREF_SELECTED_SERVICES = "selected_services";
    private int gridColumns;

    private final ExecutorService apiExecutor = Executors.newSingleThreadExecutor();
    private final ExecutorService imageExecutor = Executors.newFixedThreadPool(3);
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final List<View> posterTiles = new ArrayList<>();

    private WatchlistApiClient apiClient;
    private RemoteImageLoader imageLoader;
    private SharedPreferences preferences;
    private BrowsingState browsingState;
    private GridLayout posterGrid;
    private TextView messageView;
    private ProgressBar progressBar;
    private Button allButton;
    private Button moviesButton;
    private Button tvButton;
    private Button dateAddedButton;
    private Button alphabeticalButton;
    private TextView onPlexCheckbox;
    private TextView primeCheckbox;
    private TextView hboCheckbox;
    private TextView skyShowtimeCheckbox;
    private TextView crunchyrollCheckbox;
    private TextView unavailableCheckbox;
    private TextView contentTitleView;
    private TextView contentCountView;
    private View lastRailFocus;
    private List<WatchlistItem> loadedItems = new ArrayList<>();
    private int loadGeneration;
    private volatile boolean destroyed;
    private boolean startupAvailabilityRefreshStarted;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setTurnScreenOn(true);
        }

        apiClient = new WatchlistApiClient(WatchlistConfig.apiBaseUrl());
        imageLoader = new RemoteImageLoader(imageExecutor, () -> !destroyed);
        preferences = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        browsingState = restoreBrowsingState();
        gridColumns = WatchlistConfig.gridColumns();
        setContentView(createContentView());
        updateControlStyles();
        allButton.requestFocus();
        loadItems(true);
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        return super.dispatchKeyEvent(event);
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        loadGeneration++;
        imageLoader.discardObsoleteRequests();
        mainHandler.removeCallbacksAndMessages(null);
        apiExecutor.shutdownNow();
        imageExecutor.shutdownNow();
        super.onDestroy();
    }

    private View createContentView() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.HORIZONTAL);
        root.setBackgroundColor(Color.rgb(15, 20, 25));

        root.addView(createLeftRail(), new LinearLayout.LayoutParams(dp(176), LinearLayout.LayoutParams.MATCH_PARENT));

        LinearLayout main = new LinearLayout(this);
        main.setOrientation(LinearLayout.VERTICAL);
        main.setPadding(dp(28), dp(26), dp(34), dp(22));

        main.addView(createMainHeader());

        messageView = new TextView(this);
        messageView.setTextColor(Color.rgb(203, 213, 225));
        messageView.setTextSize(17);
        messageView.setPadding(0, dp(10), 0, dp(8));
        main.addView(messageView);

        progressBar = new ProgressBar(this);
        progressBar.setVisibility(View.GONE);
        main.addView(progressBar);

        ScrollView gridScrollView = new ScrollView(this);
        gridScrollView.setFillViewport(true);
        gridScrollView.setClipToPadding(false);
        gridScrollView.setFocusable(false);

        posterGrid = new GridLayout(this);
        posterGrid.setColumnCount(gridColumns);
        posterGrid.setAlignmentMode(GridLayout.ALIGN_BOUNDS);
        posterGrid.setPadding(0, dp(8), 0, dp(18));
        gridScrollView.addView(posterGrid, new ScrollView.LayoutParams(
                ScrollView.LayoutParams.MATCH_PARENT,
                ScrollView.LayoutParams.WRAP_CONTENT));
        main.addView(gridScrollView, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                0,
                1));

        root.addView(main, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
        return root;
    }

    private LinearLayout createLeftRail() {
        LinearLayout rail = new LinearLayout(this);
        rail.setOrientation(LinearLayout.VERTICAL);
        rail.setPadding(dp(10), dp(18), dp(10), dp(18));
        rail.setBackgroundColor(Color.rgb(18, 24, 31));

        TextView heading = new TextView(this);
        heading.setText(R.string.app_name);
        heading.setTextColor(Color.WHITE);
        heading.setTextSize(19);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        heading.setPadding(dp(8), 0, dp(8), dp(16));
        rail.addView(heading);

        allButton = railButton(getString(R.string.nav_all));
        allButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_ALL));
        rail.addView(allButton);

        moviesButton = railButton(getString(R.string.nav_movies));
        moviesButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_MOVIES));
        rail.addView(moviesButton);

        tvButton = railButton(getString(R.string.nav_tv_shows));
        tvButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_TV));
        rail.addView(tvButton);

        rail.addView(spacer(1, dp(18)));

        onPlexCheckbox = railCheckbox(getString(R.string.rail_on_plex));
        onPlexCheckbox.setOnClickListener(view -> toggleAvailabilityService(BrowsingState.SERVICE_PLEX));
        rail.addView(onPlexCheckbox);

        primeCheckbox = railCheckbox(getString(R.string.rail_prime));
        primeCheckbox.setOnClickListener(view -> toggleAvailabilityService(BrowsingState.SERVICE_PRIME));
        rail.addView(primeCheckbox);

        hboCheckbox = railCheckbox(getString(R.string.rail_hbo));
        hboCheckbox.setOnClickListener(view -> toggleAvailabilityService(BrowsingState.SERVICE_HBO));
        rail.addView(hboCheckbox);

        skyShowtimeCheckbox = railCheckbox(getString(R.string.rail_skyshowtime));
        skyShowtimeCheckbox.setOnClickListener(view -> toggleAvailabilityService(BrowsingState.SERVICE_SKYSHOWTIME));
        rail.addView(skyShowtimeCheckbox);

        crunchyrollCheckbox = railCheckbox(getString(R.string.rail_crunchyroll));
        crunchyrollCheckbox.setOnClickListener(view -> toggleAvailabilityService(BrowsingState.SERVICE_CRUNCHYROLL));
        rail.addView(crunchyrollCheckbox);

        unavailableCheckbox = railCheckbox(getString(R.string.rail_unavailable));
        unavailableCheckbox.setOnClickListener(view -> toggleUnavailable());
        rail.addView(unavailableCheckbox);

        rail.addView(spacer(1, 0), new LinearLayout.LayoutParams(1, 0, 1));

        ImageButton searchButton = iconButton(R.drawable.ic_search, getString(R.string.action_search));
        searchButton.setEnabled(false);
        rail.addView(searchButton);

        wireRailFocusLinks(searchButton);
        return rail;
    }

    private LinearLayout createMainHeader() {
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);

        LinearLayout titleBlock = new LinearLayout(this);
        titleBlock.setOrientation(LinearLayout.VERTICAL);

        contentTitleView = new TextView(this);
        contentTitleView.setTextColor(Color.WHITE);
        contentTitleView.setTextSize(24);
        contentTitleView.setTypeface(Typeface.DEFAULT_BOLD);
        titleBlock.addView(contentTitleView);

        contentCountView = new TextView(this);
        contentCountView.setTextColor(Color.rgb(148, 163, 184));
        contentCountView.setTextSize(13);
        titleBlock.addView(contentCountView);

        header.addView(titleBlock, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));

        dateAddedButton = textButton(getString(R.string.sort_date_added));
        dateAddedButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_DATE_ADDED));
        header.addView(dateAddedButton);

        alphabeticalButton = textButton(getString(R.string.sort_alphabetical));
        alphabeticalButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_ALPHABETICAL));
        header.addView(alphabeticalButton);

        dateAddedButton.setNextFocusRightId(alphabeticalButton.getId());
        alphabeticalButton.setNextFocusLeftId(dateAddedButton.getId());
        return header;
    }

    private void wireRailFocusLinks(View searchButton) {
        allButton.setNextFocusDownId(moviesButton.getId());
        moviesButton.setNextFocusUpId(allButton.getId());
        moviesButton.setNextFocusDownId(tvButton.getId());
        tvButton.setNextFocusUpId(moviesButton.getId());
        tvButton.setNextFocusDownId(onPlexCheckbox.getId());
        onPlexCheckbox.setNextFocusUpId(tvButton.getId());
        onPlexCheckbox.setNextFocusDownId(primeCheckbox.getId());
        primeCheckbox.setNextFocusUpId(onPlexCheckbox.getId());
        primeCheckbox.setNextFocusDownId(hboCheckbox.getId());
        hboCheckbox.setNextFocusUpId(primeCheckbox.getId());
        hboCheckbox.setNextFocusDownId(skyShowtimeCheckbox.getId());
        skyShowtimeCheckbox.setNextFocusUpId(hboCheckbox.getId());
        skyShowtimeCheckbox.setNextFocusDownId(crunchyrollCheckbox.getId());
        crunchyrollCheckbox.setNextFocusUpId(skyShowtimeCheckbox.getId());
        crunchyrollCheckbox.setNextFocusDownId(unavailableCheckbox.getId());
        unavailableCheckbox.setNextFocusUpId(crunchyrollCheckbox.getId());
        unavailableCheckbox.setNextFocusDownId(searchButton.getId());
        searchButton.setNextFocusUpId(unavailableCheckbox.getId());
    }

    private void selectMediaType(String mediaType) {
        if (mediaType.equals(browsingState.mediaType())) {
            return;
        }
        browsingState = browsingState.withMediaType(mediaType).withFocusedItemId(null);
        persistBrowsingState();
        updateControlStyles();
        loadItems(false);
    }

    private void openDetails(WatchlistItem item) {
        browsingState = browsingState.withFocusedItemId(item.id());
        persistBrowsingState();
        Intent intent = new Intent(this, DetailsActivity.class);
        intent.putExtra(DetailsActivity.EXTRA_ITEM, item);
        startActivity(intent);
    }

    private void toggleAvailabilityService(String service) {
        browsingState = browsingState
                .withAvailabilityServiceSelection(service, !browsingState.isAvailabilityServiceSelected(service))
                .withFocusedItemId(null);
        persistBrowsingState();
        updateControlStyles();
        renderItems(loadedItems, false);
    }

    private void toggleUnavailable() {
        browsingState = browsingState
                .withIncludeUnavailable(!browsingState.includeUnavailable())
                .withFocusedItemId(null);
        persistBrowsingState();
        updateControlStyles();
        loadItems(false);
    }

    private void selectSortMode(String sortMode) {
        if (sortMode.equals(browsingState.sortMode())) {
            return;
        }
        browsingState = browsingState.withSortMode(sortMode);
        persistBrowsingState();
        updateControlStyles();
        loadItems(false);
    }

    private void loadItems() {
        loadItems(false);
    }

    private void loadItems(boolean requestStartupAvailabilityRefresh) {
        if (destroyed) {
            return;
        }
        int generation = ++loadGeneration;
        String mediaType = browsingState.mediaType();
        String sortMode = browsingState.sortMode();
        boolean includeUnavailable = browsingState.includeUnavailable();
        showLoading();
        try {
            apiExecutor.execute(() -> {
                try {
                    List<WatchlistItem> items = apiClient.getWatchlist(
                            mediaType,
                            sortMode,
                            includeUnavailable);
                    if (!destroyed) {
                        mainHandler.post(() -> {
                            if (!destroyed && generation == loadGeneration) {
                                loadedItems = items;
                                renderItems(items, true);
                                if (requestStartupAvailabilityRefresh) {
                                    refreshAvailabilityAfterInitialLoad(generation);
                                }
                            }
                        });
                    }
                } catch (Exception exception) {
                    if (!destroyed) {
                        mainHandler.post(() -> {
                            if (!destroyed && generation == loadGeneration) {
                                showError(exception);
                            }
                        });
                    }
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The Activity is tearing down.
        }
    }

    private void refreshAvailabilityAfterInitialLoad(int initialGeneration) {
        if (startupAvailabilityRefreshStarted || destroyed || initialGeneration != loadGeneration) {
            return;
        }

        startupAvailabilityRefreshStarted = true;
        try {
            apiExecutor.execute(() -> {
                try {
                    AvailabilityRefreshResult result = apiClient.refreshAvailability();
                    if (!destroyed && result.ranPlexSync()) {
                        mainHandler.post(() -> {
                            if (!destroyed) {
                                loadItems(false);
                            }
                        });
                    }
                } catch (Exception ignored) {
                    // Cached data is already visible; startup refresh must not replace it with an error.
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The Activity is tearing down.
        }
    }

    private void renderItems(List<WatchlistItem> items, boolean restoreFocus) {
        if (destroyed) {
            return;
        }
        progressBar.setVisibility(View.GONE);
        clearPosterGrid();
        updateHeaderText();

        List<WatchlistItem> visibleItems = WatchlistFilters.applyAvailabilityFilters(
                items,
                browsingState.mediaType(),
                browsingState.selectedAvailabilityServices(),
                browsingState.includeUnavailable());
        if (visibleItems.isEmpty()) {
            messageView.setText(R.string.message_empty);
            return;
        }

        messageView.setText("");
        for (WatchlistItem item : visibleItems) {
            View tile = createPosterTile(item);
            posterTiles.add(tile);
            posterGrid.addView(tile);
        }

        wirePosterFocusLinks();
        if (restoreFocus) {
            restorePosterFocus(visibleItems);
        }
    }

    private View createPosterTile(WatchlistItem item) {
        int posterGeneration = imageLoader.currentGeneration();
        LinearLayout tile = new LinearLayout(this);
        tile.setId(View.generateViewId());
        tile.setOrientation(LinearLayout.VERTICAL);
        tile.setFocusable(true);
        tile.setClickable(true);
        tile.setPadding(dp(5), dp(5), dp(5), dp(6));
        tile.setBackground(tileBackground(false));
        tile.setOnClickListener(view -> openDetails(item));
        tile.setOnFocusChangeListener((view, hasFocus) -> {
            view.setBackground(tileBackground(hasFocus));
            view.animate()
                    .scaleX(hasFocus ? 1.035f : 1f)
                    .scaleY(hasFocus ? 1.035f : 1f)
                    .setDuration(120)
                    .start();
            if (hasFocus) {
                browsingState = browsingState.withFocusedItemId(item.id());
                persistBrowsingState();
            }
        });

        FrameLayout artworkFrame = new FrameLayout(this);
        artworkFrame.setBackgroundColor(Color.rgb(42, 48, 56));
        tile.addView(artworkFrame, new LinearLayout.LayoutParams(dp(118), dp(168)));

        ImageView artwork = new ImageView(this);
        artwork.setScaleType(ImageView.ScaleType.CENTER_CROP);
        artworkFrame.addView(artwork, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        TextView missingArtwork = new TextView(this);
        missingArtwork.setText(R.string.message_artwork_unavailable);
        missingArtwork.setTextColor(Color.rgb(203, 213, 225));
        missingArtwork.setTextSize(15);
        missingArtwork.setGravity(Gravity.CENTER);
        artworkFrame.addView(missingArtwork, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        if (item.posterUrl() != null && !item.posterUrl().isEmpty()) {
            missingArtwork.setVisibility(View.GONE);
        }
        imageLoader.load(
                artwork,
                item.posterUrl(),
                Color.rgb(42, 48, 56),
                loaded -> {
                    if (imageLoader.isCurrentGeneration(posterGeneration)) {
                        missingArtwork.setVisibility(loaded ? View.GONE : View.VISIBLE);
                    }
                });

        TextView title = new TextView(this);
        title.setText(item.title());
        title.setTextColor(Color.WHITE);
        title.setTextSize(15);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setMaxLines(2);
        title.setEllipsize(TextUtils.TruncateAt.END);
        title.setGravity(Gravity.CENTER_VERTICAL);
        tile.addView(title, new LinearLayout.LayoutParams(dp(118), dp(36)));

        TextView badge = new TextView(this);
        badge.setText(formatAvailability(item));
        badge.setTextColor(Color.WHITE);
        badge.setTextSize(12);
        badge.setMaxLines(1);
        badge.setGravity(Gravity.CENTER);
        badge.setBackground(badgeBackground(item));
        tile.addView(badge, new LinearLayout.LayoutParams(dp(118), dp(24)));

        GridLayout.LayoutParams layoutParams = new GridLayout.LayoutParams();
        layoutParams.width = dp(128);
        layoutParams.height = dp(236);
        layoutParams.setMargins(0, 0, dp(10), dp(10));
        tile.setLayoutParams(layoutParams);
        return tile;
    }

    private void wirePosterFocusLinks() {
        View railTarget = lastRailFocus != null ? lastRailFocus : allButton;

        for (int index = 0; index < posterTiles.size(); index++) {
            View tile = posterTiles.get(index);
            int column = index % gridColumns;
            int previous = index - 1;
            int next = index + 1;
            int above = index - gridColumns;
            int below = index + gridColumns;

            tile.setNextFocusLeftId(column > 0 && previous >= 0
                    ? posterTiles.get(previous).getId()
                    : railTarget.getId());
            tile.setNextFocusRightId(column < gridColumns - 1 && next < posterTiles.size()
                    ? posterTiles.get(next).getId()
                    : tile.getId());

            View headerTarget = column < Math.max(1, gridColumns - 2) ? dateAddedButton : alphabeticalButton;
            tile.setNextFocusUpId(above >= 0
                    ? posterTiles.get(above).getId()
                    : headerTarget.getId());
            tile.setNextFocusDownId(below < posterTiles.size()
                    ? posterTiles.get(below).getId()
                    : tile.getId());
            tile.setOnKeyListener((view, keyCode, event) -> {
                if (event.getAction() != KeyEvent.ACTION_DOWN) {
                    return false;
                }
                View target = null;
                if (keyCode == KeyEvent.KEYCODE_DPAD_LEFT) {
                    target = column > 0 ? posterTiles.get(previous) : railTarget;
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_RIGHT) {
                    target = column < gridColumns - 1 && next < posterTiles.size()
                            ? posterTiles.get(next)
                            : view;
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_UP) {
                    View upHeaderTarget = column < Math.max(1, gridColumns - 2) ? dateAddedButton : alphabeticalButton;
                    target = above >= 0 ? posterTiles.get(above) : upHeaderTarget;
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_DOWN) {
                    target = below < posterTiles.size() ? posterTiles.get(below) : view;
                }
                if (target == null) {
                    return false;
                }
                return target == view || target.requestFocus();
            });
        }

        if (!posterTiles.isEmpty()) {
            View gridTarget = focusedPosterOrFirst();
            allButton.setNextFocusRightId(gridTarget.getId());
            moviesButton.setNextFocusRightId(gridTarget.getId());
            tvButton.setNextFocusRightId(gridTarget.getId());
            onPlexCheckbox.setNextFocusRightId(gridTarget.getId());
            primeCheckbox.setNextFocusRightId(gridTarget.getId());
            hboCheckbox.setNextFocusRightId(gridTarget.getId());
            skyShowtimeCheckbox.setNextFocusRightId(gridTarget.getId());
            crunchyrollCheckbox.setNextFocusRightId(gridTarget.getId());
            unavailableCheckbox.setNextFocusRightId(gridTarget.getId());

            dateAddedButton.setNextFocusDownId(posterTiles.get(0).getId());
            alphabeticalButton.setNextFocusDownId(posterTiles.get(Math.min(gridColumns - 1, posterTiles.size() - 1)).getId());
        }
    }

    private View focusedPosterOrFirst() {
        String focusedItemId = browsingState.focusedItemId();
        if (focusedItemId != null) {
            for (int index = 0; index < loadedItems.size() && index < posterTiles.size(); index++) {
                if (focusedItemId.equals(loadedItems.get(index).id())) {
                    return posterTiles.get(index);
                }
            }
        }
        return posterTiles.get(0);
    }

    private void restorePosterFocus(List<WatchlistItem> visibleItems) {
        int focusIndex = 0;
        String focusedItemId = browsingState.focusedItemId();
        if (focusedItemId != null) {
            for (int index = 0; index < visibleItems.size(); index++) {
                if (focusedItemId.equals(visibleItems.get(index).id())) {
                    focusIndex = index;
                    break;
                }
            }
        }
        posterTiles.get(focusIndex).requestFocus();
    }

    private void showLoading() {
        progressBar.setVisibility(View.VISIBLE);
        messageView.setText(R.string.message_loading);
        loadedItems = new ArrayList<>();
        clearPosterGrid();
        updateHeaderText();
    }

    private void showError(Exception exception) {
        progressBar.setVisibility(View.GONE);
        clearPosterGrid();
        messageView.setText(getString(
                R.string.message_backend_error,
                WatchlistConfig.apiBaseUrl(),
                exception.getMessage()));
        updateHeaderText();
    }

    private void clearPosterGrid() {
        imageLoader.discardObsoleteRequests();
        posterGrid.removeAllViews();
        posterTiles.clear();
        dateAddedButton.setNextFocusDownId(View.NO_ID);
        alphabeticalButton.setNextFocusDownId(View.NO_ID);
        allButton.setNextFocusRightId(View.NO_ID);
        moviesButton.setNextFocusRightId(View.NO_ID);
        tvButton.setNextFocusRightId(View.NO_ID);
        onPlexCheckbox.setNextFocusRightId(View.NO_ID);
        primeCheckbox.setNextFocusRightId(View.NO_ID);
        hboCheckbox.setNextFocusRightId(View.NO_ID);
        skyShowtimeCheckbox.setNextFocusRightId(View.NO_ID);
        crunchyrollCheckbox.setNextFocusRightId(View.NO_ID);
        unavailableCheckbox.setNextFocusRightId(View.NO_ID);
    }

    private Set<String> restoreSelectedServices(BrowsingState defaults) {
        String encoded = preferences.getString(PREF_SELECTED_SERVICES, null);
        if (encoded == null || encoded.isEmpty()) {
            return defaults.selectedAvailabilityServices();
        }

        Set<String> selected = new LinkedHashSet<>();
        for (String service : encoded.split(",")) {
            if (isKnownAvailabilityService(service)) {
                selected.add(service);
            }
        }
        return selected;
    }

    private static boolean isKnownAvailabilityService(String service) {
        return BrowsingState.SERVICE_PLEX.equals(service)
                || BrowsingState.SERVICE_PRIME.equals(service)
                || BrowsingState.SERVICE_HBO.equals(service)
                || BrowsingState.SERVICE_SKYSHOWTIME.equals(service)
                || BrowsingState.SERVICE_CRUNCHYROLL.equals(service);
    }

    private static String encodeSelectedServices(Set<String> selectedServices) {
        return TextUtils.join(",", selectedServices);
    }

    private BrowsingState restoreBrowsingState() {
        BrowsingState defaults = BrowsingState.defaults();
        String mediaType = preferences.getString(PREF_MEDIA_TYPE, defaults.mediaType());
        if (!BrowsingState.MEDIA_ALL.equals(mediaType)
                && !BrowsingState.MEDIA_MOVIES.equals(mediaType)
                && !BrowsingState.MEDIA_TV.equals(mediaType)) {
            mediaType = defaults.mediaType();
        }

        String sortMode = preferences.getString(PREF_SORT_MODE, defaults.sortMode());
        if (!CollectionOrganizer.SORT_DATE_ADDED.equals(sortMode)
                && !CollectionOrganizer.SORT_ALPHABETICAL.equals(sortMode)) {
            sortMode = defaults.sortMode();
        }

        String focusedItemId = preferences.getString(PREF_FOCUSED_ITEM_ID, defaults.focusedItemId());
        if (focusedItemId != null && focusedItemId.isEmpty()) {
            focusedItemId = defaults.focusedItemId();
        }

        return defaults
                .withMediaType(mediaType)
                .withSortMode(sortMode)
                .withIncludeUnavailable(preferences.getBoolean(
                        PREF_INCLUDE_UNAVAILABLE,
                        defaults.includeUnavailable()))
                .withSelectedAvailabilityServices(restoreSelectedServices(defaults))
                .withFocusedItemId(focusedItemId);
    }

    private void persistBrowsingState() {
        SharedPreferences.Editor editor = preferences.edit()
                .putString(PREF_MEDIA_TYPE, browsingState.mediaType())
                .putString(PREF_SORT_MODE, browsingState.sortMode())
                .putBoolean(PREF_INCLUDE_UNAVAILABLE, browsingState.includeUnavailable())
                .putString(PREF_SELECTED_SERVICES, encodeSelectedServices(browsingState.selectedAvailabilityServices()));
        if (browsingState.focusedItemId() == null) {
            editor.remove(PREF_FOCUSED_ITEM_ID);
        } else {
            editor.putString(PREF_FOCUSED_ITEM_ID, browsingState.focusedItemId());
        }
        editor.apply();
    }

    private void updateControlStyles() {
        styleTextButton(allButton, BrowsingState.MEDIA_ALL.equals(browsingState.mediaType()));
        styleTextButton(moviesButton, BrowsingState.MEDIA_MOVIES.equals(browsingState.mediaType()));
        styleTextButton(tvButton, BrowsingState.MEDIA_TV.equals(browsingState.mediaType()));
        styleCheckbox(onPlexCheckbox, browsingState.isAvailabilityServiceSelected(BrowsingState.SERVICE_PLEX));
        styleCheckbox(primeCheckbox, browsingState.isAvailabilityServiceSelected(BrowsingState.SERVICE_PRIME));
        styleCheckbox(hboCheckbox, browsingState.isAvailabilityServiceSelected(BrowsingState.SERVICE_HBO));
        styleCheckbox(skyShowtimeCheckbox, browsingState.isAvailabilityServiceSelected(BrowsingState.SERVICE_SKYSHOWTIME));
        styleCheckbox(crunchyrollCheckbox, browsingState.isAvailabilityServiceSelected(BrowsingState.SERVICE_CRUNCHYROLL));
        styleCheckbox(unavailableCheckbox, browsingState.includeUnavailable());
        styleTextButton(dateAddedButton, CollectionOrganizer.SORT_DATE_ADDED.equals(browsingState.sortMode()));
        styleTextButton(alphabeticalButton, CollectionOrganizer.SORT_ALPHABETICAL.equals(browsingState.sortMode()));
        updateHeaderText();
    }

    private void updateHeaderText() {
        if (contentTitleView == null || contentCountView == null) {
            return;
        }

        if (BrowsingState.MEDIA_MOVIES.equals(browsingState.mediaType())) {
            contentTitleView.setText(R.string.content_title_movies);
        } else if (BrowsingState.MEDIA_TV.equals(browsingState.mediaType())) {
            contentTitleView.setText(R.string.content_title_tv);
        } else {
            contentTitleView.setText(R.string.content_title_all);
        }

        int visibleCount = WatchlistFilters.applyAvailabilityFilters(
                loadedItems,
                browsingState.mediaType(),
                browsingState.selectedAvailabilityServices(),
                browsingState.includeUnavailable()).size();
        contentCountView.setText(getString(R.string.content_count, visibleCount));
    }

    private Button railButton(String text) {
        Button button = textButton(text);
        button.setGravity(Gravity.CENTER_VERTICAL);
        button.setMinWidth(0);
        button.setPadding(dp(12), 0, dp(12), 0);
        button.setOnFocusChangeListener((view, hasFocus) -> {
            if (hasFocus) {
                lastRailFocus = view;
            }
            updateControlStyles();
        });
        return button;
    }

    private TextView railCheckbox(String text) {
        TextView checkbox = new TextView(this);
        checkbox.setId(View.generateViewId());
        checkbox.setText(text);
        checkbox.setTextColor(Color.WHITE);
        checkbox.setTextSize(16);
        checkbox.setGravity(Gravity.CENTER_VERTICAL);
        checkbox.setFocusable(true);
        checkbox.setClickable(true);
        checkbox.setMinHeight(dp(42));
        checkbox.setPadding(dp(10), 0, dp(10), 0);
        checkbox.setBackground(controlBackground(false, false));
        checkbox.setOnFocusChangeListener((view, hasFocus) -> {
            if (hasFocus) {
                lastRailFocus = view;
            }
            updateControlStyles();
        });
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(42));
        params.setMargins(0, 0, 0, dp(6));
        checkbox.setLayoutParams(params);
        return checkbox;
    }

    private Button textButton(String text) {
        Button button = new Button(this);
        button.setId(View.generateViewId());
        button.setText(text);
        button.setAllCaps(false);
        button.setTextSize(16);
        button.setTextColor(Color.WHITE);
        button.setFocusable(true);
        button.setMinWidth(dp(118));
        button.setMinHeight(dp(48));
        button.setBackground(controlBackground(false, false));
        button.setOnFocusChangeListener((view, hasFocus) -> updateControlStyles());
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT,
                dp(48));
        params.setMargins(0, 0, dp(10), 0);
        button.setLayoutParams(params);
        return button;
    }

    private ImageButton iconButton(int drawableId, String description) {
        ImageButton button = new ImageButton(this);
        button.setId(View.generateViewId());
        button.setImageResource(drawableId);
        button.setContentDescription(description);
        button.setColorFilter(Color.WHITE);
        button.setFocusable(true);
        button.setBackground(controlBackground(false, false));
        button.setOnFocusChangeListener((view, hasFocus) ->
                view.setBackground(controlBackground(false, hasFocus)));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(dp(48), dp(48));
        params.setMargins(0, 0, dp(10), 0);
        button.setLayoutParams(params);
        return button;
    }

    private void styleTextButton(Button button, boolean selected) {
        button.setTextColor(selected ? Color.rgb(15, 20, 25) : Color.WHITE);
        button.setBackground(controlBackground(selected, button.hasFocus()));
    }

    private void styleCheckbox(TextView checkbox, boolean checked) {
        checkbox.setText((checked ? "[x] " : "[ ] ") + checkboxLabel(checkbox));
        checkbox.setTextColor(Color.WHITE);
        checkbox.setBackground(controlBackground(checked, checkbox.hasFocus()));
    }

    private String checkboxLabel(TextView checkbox) {
        CharSequence text = checkbox.getText();
        String value = text == null ? "" : text.toString();
        if (value.startsWith("[x] ") || value.startsWith("[ ] ")) {
            return value.substring(4);
        }
        return value;
    }

    private GradientDrawable controlBackground(boolean selected, boolean focused) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(selected ? Color.rgb(103, 232, 249) : Color.rgb(43, 53, 64));
        drawable.setCornerRadius(dp(5));
        drawable.setStroke(
                dp(focused ? 3 : 1),
                focused ? Color.WHITE : selected ? Color.rgb(165, 243, 252) : Color.rgb(75, 85, 99));
        return drawable;
    }

    private GradientDrawable tileBackground(boolean focused) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(Color.rgb(28, 36, 44));
        drawable.setCornerRadius(dp(5));
        drawable.setStroke(dp(focused ? 4 : 1), focused ? Color.WHITE : Color.rgb(75, 85, 99));
        return drawable;
    }

    private GradientDrawable badgeBackground(WatchlistItem item) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setCornerRadius(dp(3));
        int color;
        if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
            color = Color.rgb(20, 120, 80);
        } else if (!item.ownedServiceAvailability().isEmpty()) {
            color = Color.rgb(14, 116, 144);
        } else {
            color = Color.rgb(86, 99, 112);
        }
        drawable.setColor(color);
        return drawable;
    }

    static String formatAvailability(WatchlistItem item) {
        if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
            return "On Plex";
        }
        String providerBadge = formatOwnedProviderBadge(item);
        if (providerBadge != null) {
            return providerBadge;
        }
        if (item.vodReleaseKnown() && !item.releasedOnVod()) {
            return "Not released";
        }
        if ("unreleased".equals(item.availabilityStatus())) {
            return "Unreleased";
        }
        if ("unknown_match".equals(item.availabilityStatus())) {
            return "Match uncertain";
        }
        return "Unavailable";
    }

    private static String formatOwnedProviderBadge(WatchlistItem item) {
        if (item.ownedServiceAvailability().isEmpty()) {
            return null;
        }

        String first = shortProviderName(item.ownedServiceAvailability().get(0));
        int count = item.ownedServiceAvailability().size();
        if (count == 1) {
            return first;
        }

        if (count == 2 && first.length() <= 10) {
            return first + " +1";
        }

        return count + " services";
    }

    private static String shortProviderName(String providerName) {
        if ("Amazon Prime Video".equalsIgnoreCase(providerName)
                || "Prime Video".equalsIgnoreCase(providerName)) {
            return "Prime";
        }
        if ("Max".equalsIgnoreCase(providerName)
                || "HBO Max".equalsIgnoreCase(providerName)) {
            return "Max";
        }
        if ("SkyShowtime".equalsIgnoreCase(providerName)) {
            return "SkyShowtime";
        }
        if ("Crunchyroll".equalsIgnoreCase(providerName)) {
            return "Crunchyroll";
        }

        return providerName;
    }

    private View spacer(int width, int height) {
        View view = new View(this);
        view.setLayoutParams(new LinearLayout.LayoutParams(width, height));
        return view;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density);
    }
}
