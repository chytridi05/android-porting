package fixture.route;

import com.badlogic.gdx.ApplicationAdapter;
import com.badlogic.gdx.Gdx;
import com.badlogic.gdx.Input;
import com.badlogic.gdx.graphics.glutils.ShapeRenderer;
import com.badlogic.gdx.utils.ScreenUtils;

public final class PathGame extends ApplicationAdapter {
    private ShapeRenderer shapes;
    private String route;
    private int step;
    private int x;
    private int y;

    @Override
    public void create() {
        route = Gdx.files.internal("levels/route.txt").readString("UTF-8").trim();
        if (route.isEmpty() || !route.matches("[RU]+")) {
            throw new IllegalArgumentException("Expected a route made of R and U steps");
        }
        shapes = new ShapeRenderer();
        Gdx.graphics.setTitle("OBB Route Ready");
    }

    @Override
    public void render() {
        if (step < route.length()) {
            if (Gdx.input.isKeyJustPressed(Input.Keys.RIGHT)) {
                move('R');
            } else if (Gdx.input.isKeyJustPressed(Input.Keys.UP)) {
                move('U');
            } else if (Gdx.input.justTouched()) {
                move(Gdx.input.getX() >= Gdx.graphics.getWidth() / 2 ? 'R' : 'U');
            }
        }

        ScreenUtils.clear(0.08f, 0.12f, 0.18f, 1.0f);
        shapes.begin(ShapeRenderer.ShapeType.Filled);
        shapes.setColor(0.26f, 0.76f, 0.60f, 1.0f);
        int pathX = 0;
        int pathY = 0;
        for (int index = 0; index < route.length(); index++) {
            if (route.charAt(index) == 'R') {
                pathX++;
            } else {
                pathY++;
            }
            shapes.rect(26.0f + pathX * 48.0f, 26.0f + pathY * 48.0f, 36.0f, 36.0f);
        }
        shapes.setColor(1.0f, 0.78f, 0.26f, 1.0f);
        shapes.rect(30.0f + x * 48.0f, 30.0f + y * 48.0f, 28.0f, 28.0f);
        shapes.end();
    }

    private void move(char direction) {
        if (route.charAt(step) != direction) {
            step = 0;
            x = 0;
            y = 0;
            Gdx.graphics.setTitle("OBB Route Reset");
            return;
        }

        if (direction == 'R') {
            x++;
        } else {
            y++;
        }
        step++;
        if (step == route.length()) {
            Gdx.graphics.setTitle("OBB Route Complete " + route);
        } else {
            Gdx.graphics.setTitle("OBB Route Step " + step);
        }
    }

    @Override
    public void dispose() {
        shapes.dispose();
    }
}
