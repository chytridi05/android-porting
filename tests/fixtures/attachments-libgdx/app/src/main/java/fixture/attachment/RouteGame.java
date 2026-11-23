package fixture.attachment;

import com.badlogic.gdx.ApplicationAdapter;
import com.badlogic.gdx.Gdx;
import com.badlogic.gdx.Input;
import com.badlogic.gdx.utils.ScreenUtils;

public final class RouteGame extends ApplicationAdapter {
    private String route;
    private int step;

    @Override
    public void create() {
        route = Gdx.files.internal("levels/route.txt").readString("UTF-8").trim();
        if (Gdx.files.internal("levels/feature.txt").exists()) {
            route = RouteStep.addStep(route);
        }
        Gdx.graphics.setTitle("Attachment Route Ready " + route);
    }

    @Override
    public void render() {
        ScreenUtils.clear(0.08f, 0.12f, 0.18f, 1.0f);
        char move = Gdx.input.isKeyJustPressed(Input.Keys.RIGHT) ? 'R'
            : Gdx.input.isKeyJustPressed(Input.Keys.UP) ? 'U' : ' ';
        if (move != ' ' && step < route.length()) {
            step = route.charAt(step) == move ? step + 1 : 0;
            Gdx.graphics.setTitle(step == route.length() ? "Attachment Route Complete " + route : "Attachment Route Step " + step);
        }
    }
}
