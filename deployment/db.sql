DROP TABLE IF EXISTS POST_COMMENT CASCADE;
DROP TABLE IF EXISTS POST CASCADE;
DROP TABLE IF EXISTS BOARD_CHAT CASCADE;
DROP TABLE IF EXISTS BOARD CASCADE;
DROP TABLE IF EXISTS SCAN CASCADE;
DROP TABLE IF EXISTS MOUNTAIN CASCADE;
DROP TABLE IF EXISTS "users" CASCADE;


-- Enable UUID generation if needed
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ======================
-- TABLE: "users"
-- ======================

CREATE TABLE "users" (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    username VARCHAR(50) NOT NULL,
    password_hash char(60) NOT NULL,
    role VARCHAR(20) NOT NULL
);

-- ======================
-- TABLE: MOUNTAIN
-- ======================

CREATE TABLE MOUNTAIN (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(50) NOT NULL,
    height INT NOT NULL,
    region_id INT NOT NULL,
	lat DECIMAL(9,6) NOT NULL,
	lon DECIMAL(9,6) NOT NULL,
    NFC VARCHAR(50) UNIQUE
);


-- ======================
-- TABLE: SCAN
-- ======================
CREATE TABLE SCAN (
    id SERIAL PRIMARY KEY,
    user_id UUID NOT NULL,
    mountain_id UUID NOT NULL,
    "timestamp" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_scan_user
        FOREIGN KEY (user_id)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_scan_mountain
        FOREIGN KEY (mountain_id)
        REFERENCES MOUNTAIN(id)
        ON DELETE CASCADE
);

-- ======================
-- TABLE: BOARD
-- ======================
CREATE TABLE BOARD (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    expiry_date DATE NOT NULL,
    user_id UUID NOT NULL,
    mountain_id UUID NOT NULL,
    tour_time INT NOT NULL,
    difficulty INT NOT NULL,
    description TEXT NOT NULL,

    CONSTRAINT fk_board_user
        FOREIGN KEY (user_id)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_board_mountain
        FOREIGN KEY (mountain_id)
        REFERENCES MOUNTAIN(id)
        ON DELETE CASCADE
);

-- ======================
-- TABLE: BOARD_CHAT
-- ======================
CREATE TABLE BOARD_CHAT (
    id SERIAL PRIMARY KEY,
    user_id UUID NOT NULL,
    board_id UUID NOT NULL,
    msg TEXT,
    "timestamp" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_boardchat_user
        FOREIGN KEY (user_id)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_boardchat_board
        FOREIGN KEY (board_id)
        REFERENCES BOARD(id)
        ON DELETE CASCADE
);

-- ======================
-- TABLE: POST
-- ======================
CREATE TABLE POST (
    id SERIAL PRIMARY KEY,
    created_by UUID NOT NULL,
    "timestamp" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    tagline VARCHAR(250) NOT NULL,
    start_msg TEXT NOT NULL,
    mountain_id UUID,

    CONSTRAINT fk_post_user
        FOREIGN KEY (created_by)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_post_mountain
        FOREIGN KEY (mountain_id)
        REFERENCES MOUNTAIN(id)
        ON DELETE SET NULL
);

-- ======================
-- TABLE: POST_COMMENT
-- ======================
CREATE TABLE POST_COMMENT (
    id SERIAL PRIMARY KEY,
    created_by UUID NOT NULL,
    post_id INT NOT NULL,
    "timestamp" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    message TEXT NOT NULL,

    CONSTRAINT fk_comment_user
        FOREIGN KEY (created_by)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_comment_post
        FOREIGN KEY (post_id)
        REFERENCES POST(id)
        ON DELETE CASCADE
);

INSERT INTO MOUNTAIN (name, height, region_id, Lat, Lon, NFC) VALUES
('Triglav', 2864, 1, 46.3783, 13.8367, '4539123412345678'),
('Skrlatica', 2740, 1, 46.4322, 13.8211, '4539123412345679'),
('Mangart', 2679, 2, 46.4406, 13.6542, '4539123412345680'),
('Jalovec', 2645, 2, 46.4186, 13.6828, '4539123412345681'),
('Grintovec', 2558, 6, 46.3547, 14.5369, '4539123412345682'),
('Stol', 2236, 1, 46.4336, 14.1739, '4539123412345683'),
('Krn', 2244, 2, 46.2600, 13.6583, '4539123412345684'),
('Peca', 2125, 7, 46.5042, 14.7578, '4539123412345685'),
('Veliki_Sneznik', 1796, 4, 45.5886, 14.4475, '4539123412345686'),
('Smarna_gora', 669, 5, 46.1297, 14.4539, '4539123412345687'),
('Nanos', 1262, 2, 45.7725, 14.0536, '4539123412345688'),
('Slavnik', 1028, 3, 45.5333, 13.9750, '4539123412345689'),
('Kum', 1220, 10, 46.1083, 15.0806, '4539123412345690'),
('Lisca', 948, 11, 46.0683, 15.2858, '4539123412345691'),
('Urslja_gora', 1699, 7, 46.4842, 14.9650, '4539123412345692'),
('Boc', 978, 6, 46.2892, 15.6022, '4539123412345693'),
('Donacka_gora', 883, 8, 46.2617, 15.7417, '4539123412345694'),
('Trdinov_vrh', 1178, 12, 45.7664, 15.3175, '4539123412345695'),
('Mirna_gora', 1047, 12, 45.6692, 15.1436, '4539123412345696'),
('Rogla', 1517, 6, 46.4528, 15.3339, '4539123412345697'),
('Crni_vrh', 1543, 8, 46.4833, 15.2225, '4539123412345698'),
('Blegos', 1562, 1, 46.1647, 14.1164, '4539123412345699'),
('Ratitovec', 1666, 1, 46.2231, 14.0950, '4539123412345700'),
('Porezen', 1630, 2, 46.1772, 13.9753, '4539123412345701'),
('Krim', 1107, 5, 45.9283, 14.4719, '4539123412345702'),
('Ojstrica', 2350, 6, 46.3533, 14.6547, '4539123412345703'),
('Planjava', 2394, 6, 46.3550, 14.6150, '4539123412345704'),
('Velika_Planina', 1666, 6, 46.2950, 14.6542, '4539123412345705'),
('Vogel', 1922, 1, 46.2514, 13.8364, '4539123412345706'),
('Crna_prst', 1844, 1, 46.2280, 13.9350, '4539123412345707'),
('Golica', 1835, 1, 46.4880, 14.0550, '4539123412345708'),
('Storzic', 2132, 1, 46.3510, 14.4020, '4539123412345709'),
('Mrzlica', 1122, 10, 46.1642, 15.1053, '4539123412345710'),
('Raduha', 2062, 6, 46.4136, 14.7436, '4539123412345711'),
('Vremscica', 1027, 3, 45.6880, 14.0480, '4539123412345712'),
('Grmada', 898, 5, 46.0964, 14.3314, '4539123412345713'),
('Srebrni_breg', 404, 9, 46.8453, 16.1253, '4539123412345714'),
('Veliki_Rog', 1099, 12, 45.6711, 15.0044, '4539123412345715'),
('Matajur', 1642, 2, 46.2040, 13.5510, '4539123412345716'),
('Lubnik', 1025, 1, 46.1680, 14.2690, '4539123412345717');