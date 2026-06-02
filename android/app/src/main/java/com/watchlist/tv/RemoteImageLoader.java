package com.watchlist.tv;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.widget.ImageView;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.RejectedExecutionException;

public final class RemoteImageLoader {
    public interface ActiveCheck {
        boolean isActive();
    }

    private final ExecutorService executor;
    private final ActiveCheck activeCheck;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private volatile int loadGeneration;

    public RemoteImageLoader(ExecutorService executor, ActiveCheck activeCheck) {
        this.executor = executor;
        this.activeCheck = activeCheck;
    }

    public void load(ImageView imageView, String imageUrl, int placeholderColor) {
        int generation = loadGeneration;
        imageView.setBackgroundColor(placeholderColor);
        imageView.setImageBitmap(null);

        if (imageUrl == null || imageUrl.isEmpty()) {
            return;
        }

        if (!isCurrentGeneration(generation)) {
            return;
        }

        try {
            executor.execute(() -> {
                if (!isCurrentGeneration(generation)) {
                    return;
                }
                try {
                    URL url = new URL(imageUrl);
                    HttpURLConnection connection = (HttpURLConnection) url.openConnection();
                    connection.setConnectTimeout(5000);
                    connection.setReadTimeout(5000);
                    try (InputStream stream = connection.getInputStream()) {
                        Bitmap bitmap = BitmapFactory.decodeStream(stream);
                        postIfCurrentGeneration(generation, () -> imageView.setImageBitmap(bitmap));
                    } finally {
                        connection.disconnect();
                    }
                } catch (Exception ignored) {
                    postIfCurrentGeneration(
                            generation,
                            () -> imageView.setBackgroundColor(Color.rgb(42, 48, 56)));
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The owning Activity is tearing down.
        }
    }

    public void discardObsoleteRequests() {
        loadGeneration++;
    }

    public int currentGeneration() {
        return loadGeneration;
    }

    public boolean isCurrentGeneration(int generation) {
        return activeCheck.isActive() && generation == loadGeneration;
    }

    private void postIfCurrentGeneration(int generation, Runnable callback) {
        if (isCurrentGeneration(generation)) {
            mainHandler.post(() -> {
                if (isCurrentGeneration(generation)) {
                    callback.run();
                }
            });
        }
    }
}
