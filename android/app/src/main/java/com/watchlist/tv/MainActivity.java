package com.watchlist.tv;

import android.app.Activity;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.PopupWindow;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

public final class MainActivity extends Activity {
    private static final String PREFS_NAME = "browsing_state";
    private static final String PREF_MEDIA_TYPE = "media_type";
    private static final String PREF_SORT_MODE = "sort_mode";
    private static final String PREF_INCLUDE_UNAVAILABLE = "include_unavailable";
    private static final String PREF_FOCUSED_ITEM_ID = "focused_item_id";
    private static final int GRID_COLUMNS = 5;

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
    private Button moviesButton;
    private Button tvButton;
    private Button dateAddedButton;
    private Button alphabeticalButton;
    private ImageButton filterButton;
    private PopupWindow filterPopup;
    private List<WatchlistItem> loadedItems = new ArrayList<>();
    private int loadGeneration;
    private volatile boolean destroyed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        apiClient = new WatchlistApiClient(WatchlistConfig.apiBaseUrl());
        imageLoader = new RemoteImageLoader(imageExecutor, () -> !destroyed);
        preferences = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        browsingState = restoreBrowsingState();
        setContentView(createContentView());
        updateControlStyles();
        loadItems();
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        if (event.getKeyCode() == KeyEvent.KEYCODE_BACK
                && event.getAction() == KeyEvent.ACTION_UP
                && filterPopup != null
                && filterPopup.isShowing()) {
            filterPopup.dismiss();
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        loadGeneration++;
        mainHandler.removeCallbacksAndMessages(null);
        apiExecutor.shutdownNow();
        imageExecutor.shutdownNow();
        super.onDestroy();
    }

    private View createContentView() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(44), dp(30), dp(44), dp(24));
        root.setBackgroundColor(Color.rgb(15, 20, 25));

        TextView heading = new TextView(this);
        heading.setText(R.string.app_name);
        heading.setTextColor(Color.WHITE);
        heading.setTextSize(28);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(heading);

        root.addView(createTopNavigation());
        root.addView(createToolbar());

        messageView = new TextView(this);
        messageView.setTextColor(Color.rgb(203, 213, 225));
        messageView.setTextSize(17);
        messageView.setPadding(0, dp(12), 0, dp(8));
        root.addView(messageView);

        progressBar = new ProgressBar(this);
        progressBar.setVisibility(View.GONE);
        root.addView(progressBar);

        ScrollView gridScrollView = new ScrollView(this);
        gridScrollView.setFillViewport(true);
        gridScrollView.setClipToPadding(false);
        gridScrollView.setFocusable(false);

        posterGrid = new GridLayout(this);
        posterGrid.setColumnCount(GRID_COLUMNS);
        posterGrid.setAlignmentMode(GridLayout.ALIGN_BOUNDS);
        posterGrid.setPadding(0, dp(10), 0, dp(18));
        gridScrollView.addView(posterGrid, new ScrollView.LayoutParams(
                ScrollView.LayoutParams.MATCH_PARENT,
                ScrollView.LayoutParams.WRAP_CONTENT));
        root.addView(gridScrollView, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                0,
                1));

        return root;
    }

    private LinearLayout createTopNavigation() {
        LinearLayout navigation = horizontalZone();
        navigation.setPadding(0, dp(18), 0, dp(10));

        Button allButton = textButton(getString(R.string.nav_all));
        allButton.setEnabled(false);
        navigation.addView(allButton);

        moviesButton = textButton(getString(R.string.nav_movies));
        moviesButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_MOVIES));
        navigation.addView(moviesButton);

        tvButton = textButton(getString(R.string.nav_tv_shows));
        tvButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_TV));
        navigation.addView(tvButton);

        navigation.addView(spacer(dp(12), 1));

        ImageButton searchButton = iconButton(R.drawable.ic_search, getString(R.string.action_search));
        searchButton.setEnabled(false);
        navigation.addView(searchButton);

        moviesButton.setNextFocusRightId(tvButton.getId());
        tvButton.setNextFocusLeftId(moviesButton.getId());
        return navigation;
    }

    private LinearLayout createToolbar() {
        LinearLayout toolbar = horizontalZone();
        toolbar.setPadding(0, 0, 0, dp(8));

        dateAddedButton = textButton(getString(R.string.sort_date_added));
        dateAddedButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_DATE_ADDED));
        toolbar.addView(dateAddedButton);

        alphabeticalButton = textButton(getString(R.string.sort_alphabetical));
        alphabeticalButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_ALPHABETICAL));
        toolbar.addView(alphabeticalButton);

        filterButton = iconButton(R.drawable.ic_filter, getString(R.string.action_filter));
        filterButton.setOnClickListener(view -> showFilterPopup());
        toolbar.addView(filterButton);

        dateAddedButton.setNextFocusRightId(alphabeticalButton.getId());
        alphabeticalButton.setNextFocusLeftId(dateAddedButton.getId());
        alphabeticalButton.setNextFocusRightId(filterButton.getId());
        filterButton.setNextFocusLeftId(alphabeticalButton.getId());
        updateZoneFocusLinks();
        return toolbar;
    }

    private void selectMediaType(String mediaType) {
        if (mediaType.equals(browsingState.mediaType())) {
            return;
        }
        browsingState = browsingState.withMediaType(mediaType).withFocusedItemId(null);
        persistBrowsingState();
        updateControlStyles();
        loadItems();
    }

    private void selectSortMode(String sortMode) {
        if (sortMode.equals(browsingState.sortMode())) {
            return;
        }
        browsingState = browsingState.withSortMode(sortMode);
        persistBrowsingState();
        updateControlStyles();
        renderItems(loadedItems, false);
    }

    private void showFilterPopup() {
        if (filterPopup != null && filterPopup.isShowing()) {
            filterPopup.dismiss();
            return;
        }

        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(14), dp(10), dp(18), dp(12));
        content.setBackground(panelBackground());

        CheckBox onPlex = new CheckBox(this);
        onPlex.setText(R.string.filter_on_plex);
        onPlex.setTextColor(Color.WHITE);
        onPlex.setChecked(true);
        onPlex.setEnabled(false);
        content.addView(onPlex);

        CheckBox unavailable = new CheckBox(this);
        unavailable.setText(R.string.filter_unavailable);
        unavailable.setTextColor(Color.WHITE);
        unavailable.setChecked(browsingState.includeUnavailable());
        unavailable.setOnCheckedChangeListener((buttonView, checked) -> {
            browsingState = browsingState.withIncludeUnavailable(checked);
            persistBrowsingState();
            renderItems(loadedItems, false);
        });
        content.addView(unavailable);

        filterPopup = new PopupWindow(
                content,
                dp(210),
                ViewGroup.LayoutParams.WRAP_CONTENT,
                true);
        filterPopup.setBackgroundDrawable(panelBackground());
        filterPopup.setOutsideTouchable(true);
        filterPopup.setOnDismissListener(() -> {
            if (!destroyed) {
                filterButton.requestFocus();
            }
        });
        filterPopup.showAsDropDown(filterButton, -dp(154), dp(4));
        unavailable.requestFocus();
    }

    private void loadItems() {
        if (destroyed) {
            return;
        }
        int generation = ++loadGeneration;
        String mediaType = browsingState.mediaType();
        showLoading();
        try {
            apiExecutor.execute(() -> {
                try {
                    List<WatchlistItem> items = apiClient.getWatchlist(mediaType, WatchlistFilters.FILTER_ALL);
                    if (!destroyed) {
                        mainHandler.post(() -> {
                            if (!destroyed && generation == loadGeneration) {
                                loadedItems = items;
                                renderItems(items, true);
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

    private void renderItems(List<WatchlistItem> items, boolean restoreFocus) {
        if (destroyed) {
            return;
        }
        progressBar.setVisibility(View.GONE);
        clearPosterGrid();

        List<WatchlistItem> visibleItems = CollectionOrganizer.organize(
                items,
                browsingState.includeUnavailable(),
                browsingState.sortMode());
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
        LinearLayout tile = new LinearLayout(this);
        tile.setId(View.generateViewId());
        tile.setOrientation(LinearLayout.VERTICAL);
        tile.setFocusable(true);
        tile.setClickable(true);
        tile.setPadding(dp(5), dp(5), dp(5), dp(6));
        tile.setBackground(tileBackground(false));
        tile.setOnClickListener(view -> view.requestFocus());
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
        tile.addView(artworkFrame, new LinearLayout.LayoutParams(dp(132), dp(188)));

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
            artwork.postDelayed(() -> {
                if (!destroyed && artwork.getDrawable() == null) {
                    missingArtwork.setVisibility(View.VISIBLE);
                }
            }, 10500);
        }
        imageLoader.load(artwork, item.posterUrl(), Color.rgb(42, 48, 56));

        TextView title = new TextView(this);
        title.setText(item.title());
        title.setTextColor(Color.WHITE);
        title.setTextSize(15);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setMaxLines(2);
        title.setEllipsize(TextUtils.TruncateAt.END);
        title.setGravity(Gravity.CENTER_VERTICAL);
        tile.addView(title, new LinearLayout.LayoutParams(dp(132), dp(40)));

        TextView badge = new TextView(this);
        badge.setText(formatAvailability(item));
        badge.setTextColor(Color.WHITE);
        badge.setTextSize(12);
        badge.setMaxLines(1);
        badge.setGravity(Gravity.CENTER);
        badge.setBackground(badgeBackground(item));
        tile.addView(badge, new LinearLayout.LayoutParams(dp(132), dp(24)));

        GridLayout.LayoutParams layoutParams = new GridLayout.LayoutParams();
        layoutParams.width = dp(142);
        layoutParams.height = dp(264);
        layoutParams.setMargins(0, 0, dp(12), dp(12));
        tile.setLayoutParams(layoutParams);
        return tile;
    }

    private void wirePosterFocusLinks() {
        for (int index = 0; index < posterTiles.size(); index++) {
            View tile = posterTiles.get(index);
            int column = index % GRID_COLUMNS;
            int previous = index - 1;
            int next = index + 1;
            int above = index - GRID_COLUMNS;
            int below = index + GRID_COLUMNS;

            if (column > 0 && previous >= 0) {
                tile.setNextFocusLeftId(posterTiles.get(previous).getId());
            }
            if (column < GRID_COLUMNS - 1 && next < posterTiles.size()) {
                tile.setNextFocusRightId(posterTiles.get(next).getId());
            }
            tile.setNextFocusUpId(above >= 0
                    ? posterTiles.get(above).getId()
                    : toolbarFocusTarget(column).getId());
            if (below < posterTiles.size()) {
                tile.setNextFocusDownId(posterTiles.get(below).getId());
            }
            tile.setOnKeyListener((view, keyCode, event) -> {
                if (event.getAction() != KeyEvent.ACTION_DOWN) {
                    return false;
                }
                View target = null;
                if (keyCode == KeyEvent.KEYCODE_DPAD_LEFT && column > 0) {
                    target = posterTiles.get(previous);
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_RIGHT
                        && column < GRID_COLUMNS - 1
                        && next < posterTiles.size()) {
                    target = posterTiles.get(next);
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_UP) {
                    target = above >= 0 ? posterTiles.get(above) : toolbarFocusTarget(column);
                } else if (keyCode == KeyEvent.KEYCODE_DPAD_DOWN && below < posterTiles.size()) {
                    target = posterTiles.get(below);
                }
                return target != null && target.requestFocus();
            });
        }

        if (!posterTiles.isEmpty()) {
            dateAddedButton.setNextFocusDownId(posterTiles.get(0).getId());
            alphabeticalButton.setNextFocusDownId(posterTiles.get(Math.min(2, posterTiles.size() - 1)).getId());
            filterButton.setNextFocusDownId(posterTiles.get(Math.min(4, posterTiles.size() - 1)).getId());
        }
    }

    private View toolbarFocusTarget(int column) {
        if (column >= 4) {
            return filterButton;
        }
        if (column >= 2) {
            return alphabeticalButton;
        }
        return dateAddedButton;
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
    }

    private void showError(Exception exception) {
        progressBar.setVisibility(View.GONE);
        clearPosterGrid();
        messageView.setText(getString(
                R.string.message_backend_error,
                WatchlistConfig.apiBaseUrl(),
                exception.getMessage()));
    }

    private void clearPosterGrid() {
        posterGrid.removeAllViews();
        posterTiles.clear();
        dateAddedButton.setNextFocusDownId(View.NO_ID);
        alphabeticalButton.setNextFocusDownId(View.NO_ID);
        filterButton.setNextFocusDownId(View.NO_ID);
    }

    private BrowsingState restoreBrowsingState() {
        BrowsingState defaults = BrowsingState.defaults();
        String mediaType = preferences.getString(PREF_MEDIA_TYPE, defaults.mediaType());
        if (!BrowsingState.MEDIA_MOVIES.equals(mediaType) && !BrowsingState.MEDIA_TV.equals(mediaType)) {
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
                .withFocusedItemId(focusedItemId);
    }

    private void persistBrowsingState() {
        SharedPreferences.Editor editor = preferences.edit()
                .putString(PREF_MEDIA_TYPE, browsingState.mediaType())
                .putString(PREF_SORT_MODE, browsingState.sortMode())
                .putBoolean(PREF_INCLUDE_UNAVAILABLE, browsingState.includeUnavailable());
        if (browsingState.focusedItemId() == null) {
            editor.remove(PREF_FOCUSED_ITEM_ID);
        } else {
            editor.putString(PREF_FOCUSED_ITEM_ID, browsingState.focusedItemId());
        }
        editor.apply();
    }

    private void updateControlStyles() {
        styleTextButton(moviesButton, BrowsingState.MEDIA_MOVIES.equals(browsingState.mediaType()));
        styleTextButton(tvButton, BrowsingState.MEDIA_TV.equals(browsingState.mediaType()));
        styleTextButton(dateAddedButton, CollectionOrganizer.SORT_DATE_ADDED.equals(browsingState.sortMode()));
        styleTextButton(alphabeticalButton, CollectionOrganizer.SORT_ALPHABETICAL.equals(browsingState.sortMode()));
    }

    private void updateZoneFocusLinks() {
        dateAddedButton.setNextFocusUpId(moviesButton.getId());
        alphabeticalButton.setNextFocusUpId(moviesButton.getId());
        filterButton.setNextFocusUpId(tvButton.getId());
        moviesButton.setNextFocusDownId(dateAddedButton.getId());
        tvButton.setNextFocusDownId(filterButton.getId());
    }

    private LinearLayout horizontalZone() {
        LinearLayout zone = new LinearLayout(this);
        zone.setOrientation(LinearLayout.HORIZONTAL);
        zone.setGravity(Gravity.CENTER_VERTICAL);
        return zone;
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
        drawable.setColor(WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())
                ? Color.rgb(20, 120, 80)
                : Color.rgb(86, 99, 112));
        return drawable;
    }

    private GradientDrawable panelBackground() {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(Color.rgb(31, 41, 55));
        drawable.setCornerRadius(dp(5));
        drawable.setStroke(dp(1), Color.rgb(100, 116, 139));
        return drawable;
    }

    private static String formatAvailability(WatchlistItem item) {
        if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
            return "On Plex";
        }
        if ("unreleased".equals(item.availabilityStatus())) {
            return "Unreleased";
        }
        if ("unknown_match".equals(item.availabilityStatus())) {
            return "Match uncertain";
        }
        return "Unavailable";
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
