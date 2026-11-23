package fixture.route.android;

import android.os.Bundle;
import com.badlogic.gdx.backends.android.AndroidApplication;
import com.badlogic.gdx.backends.android.AndroidApplicationConfiguration;
import com.badlogic.gdx.backends.android.AndroidFiles;
import fixture.route.PathGame;

public final class AndroidLauncher extends AndroidApplication {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        initialize(new PathGame(), new AndroidApplicationConfiguration());
        if (!((AndroidFiles) getFiles()).setAPKExpansion(1, 0)) {
            throw new IllegalStateException("Install main.1.fixture.route.obb");
        }
    }
}
