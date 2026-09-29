
const express = require("express");
const multer = require("multer");
const Database = require("better-sqlite3");
const path = require("path");
const fs = require("fs");
const crypto = require("crypto");

const app = express();
const PORT = 3000;

const ROOT = __dirname;
const STORAGE = path.join(ROOT, "storage", "jobs");
const DATA = path.join(ROOT, "data");

fs.mkdirSync(STORAGE, { recursive: true });
fs.mkdirSync(DATA, { recursive: true });

app.use(express.json());

// SQLite database stored on your PC
const db = new Database(path.join(DATA, "printjobs.db"));

db.pragma("journal_mode = WAL");

db.exec(`
  CREATE TABLE IF NOT EXISTS jobs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    token TEXT UNIQUE NOT NULL,
    original_name TEXT NOT NULL,
    stored_name TEXT NOT NULL,
    file_type TEXT NOT NULL,
    file_size INTEGER NOT NULL,
    copies INTEGER NOT NULL DEFAULT 1,
    color_mode TEXT NOT NULL DEFAULT 'BW',
    paper_size TEXT NOT NULL DEFAULT 'A4',
    status TEXT NOT NULL DEFAULT 'PENDING',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
  )
`);

// Only allow PDF, JPG, PNG uploads
const allowedTypes = {
  "application/pdf": ".pdf",
  "image/jpeg": ".jpg",
  "image/png": ".png"
};

const upload = multer({
  storage: multer.diskStorage({
    destination: (req, file, cb) => {
      cb(null, STORAGE);
    },
    filename: (req, file, cb) => {
      const ext = allowedTypes[file.mimetype];
      cb(null, crypto.randomUUID() + ext);
    }
  }),
  limits: {
    fileSize: 25 * 1024 * 1024, // 25 MB
    files: 1
  },
  fileFilter: (req, file, cb) => {
    if (!allowedTypes[file.mimetype]) {
      return cb(new Error("Only PDF, JPG and PNG files are allowed"));
    }

    cb(null, true);
  }
});

// Health check
app.get("/api/health", (req, res) => {
  res.json({
    online: true,
    service: "Xerox Print Server"
  });
});

// Upload a print job
app.post("/api/jobs", upload.single("document"), (req, res) => {
  if (!req.file) {
    return res.status(400).json({
      error: "Please select a document"
    });
  }

  const copies = Number(req.body.copies || 1);
  const colorMode = req.body.colorMode || "BW";
  const paperSize = req.body.paperSize || "A4";

  if (!Number.isInteger(copies) || copies < 1 || copies > 20) {
    fs.unlinkSync(req.file.path);
    return res.status(400).json({
      error: "Copies must be between 1 and 20"
    });
  }

  if (!["BW", "COLOR"].includes(colorMode)) {
    fs.unlinkSync(req.file.path);
    return res.status(400).json({
      error: "Invalid color mode"
    });
  }

  if (!["A4", "A3", "LETTER"].includes(paperSize)) {
    fs.unlinkSync(req.file.path);
    return res.status(400).json({
      error: "Invalid paper size"
    });
  }

  const token = crypto.randomBytes(4)
    .toString("hex")
    .toUpperCase();

  try {
    const result = db.prepare(`
      INSERT INTO jobs (
        token,
        original_name,
        stored_name,
        file_type,
        file_size,
        copies,
        color_mode,
        paper_size
      )
      VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    `).run(
      token,
      path.basename(req.file.originalname),
      req.file.filename,
      req.file.mimetype,
      req.file.size,
      copies,
      colorMode,
      paperSize
    );

    res.status(201).json({
      success: true,
      jobId: result.lastInsertRowid,
      token,
      status: "PENDING"
    });
  } catch (error) {
    fs.unlinkSync(req.file.path);
    console.error(error);

    res.status(500).json({
      error: "Could not create print job"
    });
  }
});

app.get("/api/admin/jobs", (req, res) => {
  const jobs = db.prepare(`
    SELECT
      id,
      token,
      original_name,
      stored_name,
      file_type,
      file_size,
      copies,
      color_mode,
      paper_size,
      status,
      created_at
    FROM jobs
    ORDER BY id DESC
    LIMIT 100
  `).all();

  res.json(jobs);
});

app.post("/api/admin/jobs/:id/printing", (req, res) => {
  const id = Number(req.params.id);

  if (!Number.isInteger(id) || id <= 0) {
    return res.status(400).json({
      error: "Invalid job ID"
    });
  }

  const result = db.prepare(`
    UPDATE jobs
    SET status = 'PRINTING'
    WHERE id = ? AND status = 'PENDING'
  `).run(id);

  if (result.changes === 0) {
    return res.status(409).json({
      error: "Job not found or is no longer pending"
    });
  }

  res.json({
    success: true,
    status: "PRINTING"
  });
});


app.post("/api/admin/jobs/:id/completed", (req, res) => {
  const id = Number(req.params.id);

  const result = db.prepare(`
    UPDATE jobs
    SET status = 'COMPLETED'
    WHERE id = ? AND status = 'PRINTING'
  `).run(id);

  if (result.changes === 0) {
    return res.status(409).json({
      error: "Job is not currently printing"
    });
  }

  res.json({
    success: true,
    status: "COMPLETED"
  });
});


app.post("/api/admin/jobs/:id/failed", (req, res) => {
  const id = Number(req.params.id);

  const result = db.prepare(`
    UPDATE jobs
    SET status = 'FAILED'
    WHERE id = ? AND status = 'PRINTING'
  `).run(id);

  if (result.changes === 0) {
    return res.status(409).json({
      error: "Job is not currently printing"
    });
  }

  res.json({
    success: true,
    status: "FAILED"
  });
});


// Customer checks their job status
app.get("/api/status/:token", (req, res) => {
  const job = db.prepare(`
    SELECT token, status
    FROM jobs
    WHERE token = ?
  `).get(req.params.token.toUpperCase());

  if (!job) {
    return res.status(404).json({
      error: "Job not found"
    });
  }

  res.json(job);
});


function updateJobStatus(id, status) {
  return db.prepare(`
    UPDATE jobs
    SET status = ?
    WHERE id = ? AND status = 'PENDING'
  `).run(status, id);
}

// Approve a job (records approval only for now)
app.post("/api/admin/jobs/:id/approve", (req, res) => {
  const id = Number(req.params.id);

  if (!Number.isInteger(id) || id <= 0) {
    return res.status(400).json({ error: "Invalid job ID" });
  }

  const result = updateJobStatus(id, "APPROVED");

  if (result.changes === 0) {
    return res.status(409).json({
      error: "Job not found or no longer pending"
    });
  }

  res.json({
    success: true,
    status: "APPROVED"
  });
});

// Reject a job
app.post("/api/admin/jobs/:id/reject", (req, res) => {
  const id = Number(req.params.id);

  if (!Number.isInteger(id) || id <= 0) {
    return res.status(400).json({ error: "Invalid job ID" });
  }

  const result = updateJobStatus(id, "REJECTED");

  if (result.changes === 0) {
    return res.status(409).json({
      error: "Job not found or no longer pending"
    });
  }

  res.json({
    success: true,
    status: "REJECTED"
  });
});


// Start server
app.listen(PORT, "127.0.0.1", () => {
  console.log(`Xerox server running at http://localhost:${PORT}`);
  console.log(`Files stored in: ${STORAGE}`);
});