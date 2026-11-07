namespace camera_pov
{
    // The mod's own texts in English and Portuguese, picked by the language chosen in the game's settings. Portugal's
    // Portuguese gets the Brazilian texts.
    internal static class texts
    {
        public static bool portuguese
        {
            get
            {
                Settings settings = Settings.Instance;
                string locale = settings != null ? settings.localeIsoCode : null;
                return locale != null && locale.StartsWith("pt", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string pick(string english, string portuguese_text)
        {
            return portuguese ? portuguese_text : english;
        }

        public static string mod_description => pick(
            "Walk around your park in first person, jump, or fly freely.",
            "Ande pelo parque em primeira pessoa, pule ou voe livremente.");

        public static string first_person_key_name => pick("First person", "Primeira pessoa");
        public static string first_person_key_description => pick(
            "Walks around the park at a guest's eye level. With a guest or employee selected, sees through their eyes; with a ride selected, sits in it. Press again or Esc to go back to the normal camera.",
            "Anda pelo parque na altura dos olhos de um visitante. Com um visitante ou funcionário selecionado, vê pelos olhos dele; com uma atração selecionada, senta nela. Aperte de novo ou Esc para voltar à câmera normal.");

        public static string no_seats => pick(
            "This ride has no seats to sit in yet.",
            "Essa atração ainda não tem assentos para sentar.");

        public static string fly_key_name => pick("Fly", "Voar");
        public static string fly_key_description => pick(
            "Switches between walking and flying while in first person.",
            "Alterna entre andar e voar na primeira pessoa.");

        public static string jump_key_name => pick("Jump", "Pular");
        public static string jump_key_description => pick(
            "Jumps while walking, and goes up while flying.",
            "Pula andando e sobe voando.");

        public static string eye_height_label => pick("Eye height", "Altura dos olhos");
        public static string field_of_view_label => pick("Field of view", "Campo de visão");
        public static string mouse_sensitivity_label => pick("Mouse sensitivity", "Sensibilidade do mouse");
        public static string transition_time_label => pick("Transition time (seconds)", "Duração da transição (segundos)");
        public static string invert_mouse_label => pick("Invert vertical mouse look", "Inverter o mouse na vertical");
        public static string collision_key_name => pick("Collision on/off", "Colisão liga/desliga");
        public static string collision_key_description => pick(
            "Switches whether walking runs into buildings, scenery and rides.",
            "Liga ou desliga a colisão com prédios, decorações e atrações ao andar.");
        public static string collision_on => pick("Collision: on", "Colisão: ligada");
        public static string collision_off => pick("Collision: off", "Colisão: desligada");

        public static string collision_label => pick(
            "Collide with buildings, scenery and rides while walking (flying always goes through)",
            "Colidir com prédios, decorações e atrações ao andar (voando sempre atravessa)");
        public static string controls_hint => pick(
            "Mouse looks around, the camera keys (WASD) walk, Shift runs, the mouse wheel changes the walking speed, and the collision key (Tab) switches collision. Hold Alt to free the cursor. While flying, Q or Left Ctrl goes down, E or the jump key goes up, and the mouse wheel changes the speed. Seeing through someone's eyes, the walk keys or the fly key carry on from there on your own. On a ride, the mouse wheel changes seat, Tab goes to the next car (Shift+Tab back) and the fly key flies off.",
            "O mouse olha em volta, as teclas da câmera (WASD) andam, Shift corre, a rodinha do mouse muda a velocidade ao andar e a tecla de colisão (Tab) liga e desliga a colisão. Segure Alt para soltar o cursor. Voando, Q ou Ctrl esquerdo desce, E ou a tecla de pular sobe, e a rodinha do mouse muda a velocidade. Vendo pelos olhos de alguém, as teclas de andar ou a de voar seguem dali por conta própria. Numa atração, a rodinha troca de assento, o Tab vai para o próximo carro (Shift+Tab volta) e a tecla de voar sai voando.");
    }
}
