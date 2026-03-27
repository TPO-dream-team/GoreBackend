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
	isSpam BOOL NOT NULL default FALSE,
	isSpamConfidence double precision NOT NULL default  1.,
	wasVerified BOOL NOT NULL default  FALSE,

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
	isSpam BOOL NOT NULL default  FALSE,
	isSpamConfidence double precision NOT NULL default  1.,
	wasVerified BOOL NOT NULL default  FALSE,

    CONSTRAINT fk_comment_user
        FOREIGN KEY (created_by)
        REFERENCES "users"(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_comment_post
        FOREIGN KEY (post_id)
        REFERENCES POST(id)
        ON DELETE CASCADE
);

INSERT INTO "users" (id, username, password_hash, role ) VALUES ('7a1268fd-484f-4719-8ec2-58b8c8f494f7', 'admin', '$2a$11$FKH5edK1umKbi9v.qFCc6O4zpZ/Dd.KKeMceegHjlfYVAw0TeoQfm', 'admin');

INSERT INTO MOUNTAIN (id,name, height, region_id, Lat, Lon, NFC) VALUES
('ecfe2ce3-468e-46a4-aa9e-54a1456f4e56', 'Triglav', 2864, 1, 46.378300, 13.836700, '4539123412345678'),
('ab224dfc-353a-414e-9bdd-be0e6b0f584c', 'Skrlatica', 2740, 1, 46.432200, 13.821100, '4539123412345679'),
('e96c2131-4383-4d3b-a53b-5f9200b041f3', 'Mangart', 2679, 2, 46.440600, 13.654200, '4539123412345680'),
('54dd95b4-7cd9-4119-8b4d-ee9b8d300948', 'Jalovec', 2645, 2, 46.418600, 13.682800, '4539123412345681'),
('ea54a097-0500-478f-82e8-062521a6dcff', 'Grintovec', 2558, 6, 46.354700, 14.536900, '4539123412345682'),
('52da20da-152e-4435-ade1-7e719d6e44e9', 'Stol', 2236, 1, 46.433600, 14.173900, '4539123412345683'),
('e92c1e19-59db-4e6c-8ef8-af529684d344', 'Krn', 2244, 2, 46.260000, 13.658300, '4539123412345684'),
('03ce5e02-46b1-4b37-ae78-7758a2c37fc6', 'Peca', 2125, 7, 46.504200, 14.757800, '4539123412345685'),
('24d2e9e0-958c-41de-8853-e751da168a2b', 'Veliki Sneznik', 1796, 4, 45.588600, 14.447500, '4539123412345686'),
('ba5cd381-ab16-435c-8390-363c9939aa76', 'Smarna gora', 669, 5, 46.129700, 14.453900, '4539123412345687'),
('c162beb3-f168-414e-88e6-01d459e44e02', 'Nanos', 1262, 2, 45.772500, 14.053600, '4539123412345688'),
('fbe53d80-bf21-43de-b520-89d305b4bdc2', 'Slavnik', 1028, 3, 45.533300, 13.975000, '4539123412345689'),
('53a0a23f-ceeb-4b27-a0b9-9dda9f16b5f3', 'Kum', 1220, 10, 46.108300, 15.080600, '4539123412345690'),
('06b83e6d-2a43-41dd-947e-8112c0a4c3e4', 'Lisca', 948, 11, 46.068300, 15.285800, '4539123412345691'),
('691034b3-73e8-449d-a898-51eb78aadf89', 'Urslja gora', 1699, 7, 46.484200, 14.965000, '4539123412345692'),
('85437c63-2ea7-427a-8ffb-8e67691afae0', 'Boc', 978, 6, 46.289200, 15.602200, '4539123412345693'),
('b2e95cc4-6002-490a-b43a-23aa9a6d6f1f', 'Donacka gora', 883, 8, 46.261700, 15.741700, '4539123412345694'),
('1b4cbcaa-b540-4a0f-9c73-b1cffafea0a5', 'Trdinov vrh', 1178, 12, 45.766400, 15.317500, '4539123412345695'),
('fb547a1d-3818-400a-a566-e9744587342a', 'Mirna gora', 1047, 12, 45.669200, 15.143600, '4539123412345696'),
('5fd70ff8-a7f0-4c13-992d-0a0a51718982', 'Rogla', 1517, 6, 46.452800, 15.333900, '4539123412345697'),
('73c08e26-ed41-42cd-b82b-74e0c4ea740e', 'Crni vrh', 1543, 8, 46.483300, 15.222500, '4539123412345698'),
('0cc8939d-05c6-431a-9270-1d63295b7e9d', 'Blegos', 1562, 1, 46.164700, 14.116400, '4539123412345699'),
('6154f0b3-9217-43d9-8dd6-4fed0b38d13c', 'Ratitovec', 1666, 1, 46.223100, 14.095000, '4539123412345700'),
('79623630-4032-4486-9a4b-5bf84b3c12b4', 'Porezen', 1630, 2, 46.177200, 13.975300, '4539123412345701'),
('7ef23280-bfdb-45a7-b225-ce16ea622d15', 'Krim', 1107, 5, 45.928300, 14.471900, '4539123412345702'),
('6a477fd8-afbe-4cab-9255-9cb8e15c4ed1', 'Ojstrica', 2350, 6, 46.353300, 14.654700, '4539123412345703'),
('750d4a26-82ef-4e74-8fcb-adcca50b2c60', 'Planjava', 2394, 6, 46.355000, 14.615000, '4539123412345704'),
('558b1f7f-d30e-43b9-a990-a7045db592f4', 'Velika Planina', 1666, 6, 46.295000, 14.654200, '4539123412345705'),
('5910e91b-ab52-444e-bc64-56a141e5c954', 'Vogel', 1922, 1, 46.251400, 13.836400, '4539123412345706'),
('95f8ffb3-623e-49cf-8e89-7c355f65a843', 'Crna prst', 1844, 1, 46.228000, 13.935000, '4539123412345707'),
('5d639e01-aaef-42df-bbea-eed960391daf', 'Golica', 1835, 1, 46.488000, 14.055000, '4539123412345708'),
('0e5ad448-e179-445b-8a19-4a7944e9b63f', 'Storzic', 2132, 1, 46.351000, 14.402000, '4539123412345709'),
('31750682-39b4-4d20-88c3-ad78ef9bedea', 'Mrzlica', 1122, 10, 46.164200, 15.105300, '4539123412345710'),
('533b2309-5904-44d5-b4e5-1283fab99066', 'Raduha', 2062, 6, 46.413600, 14.743600, '4539123412345711'),
('b4bd28f4-b25b-4dbb-9ab5-6b8db2f99187', 'Vremscica', 1027, 3, 45.688000, 14.048000, '4539123412345712'),
('e540ff89-bca1-48db-b792-16561928c34c', 'Grmada', 898, 5, 46.096400, 14.331400, '4539123412345713'),
('9fb146a6-5f01-40b2-8f16-75909b66ba86', 'Srebrni breg', 404, 9, 46.845300, 16.125300, '4539123412345714'),
('b684ea27-f8a0-420b-a36a-41e73283b23a', 'Veliki Rog', 1099, 12, 45.671100, 15.004400, '4539123412345715'),
('ebd259c5-a00e-40d8-a4cd-a2fb95020591', 'Matajur', 1642, 2, 46.204000, 13.551000, '4539123412345716'),
('f4c96e0c-0323-4ba7-93d3-42cf7e28c051', 'Lubnik', 1025, 1, 46.168000, 14.269000, '4539123412345717');