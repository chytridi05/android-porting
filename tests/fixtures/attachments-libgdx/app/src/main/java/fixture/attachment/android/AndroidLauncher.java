package fixture.attachment.android;

import android.os.Bundle;
import com.badlogic.gdx.backends.android.AndroidApplication;
import com.badlogic.gdx.backends.android.AndroidApplicationConfiguration;
import fixture.attachment.RouteGame;

public final class AndroidLauncher extends AndroidApplication {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        initialize(new RouteGame(), new AndroidApplicationConfiguration());
    }
}
