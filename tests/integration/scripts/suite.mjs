import { spawn } from "node:child_process";
import { mkdir, open, readFile, rm } from "node:fs/promises";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { reset, suiteLockPath } from "./reset.mjs";

const integrationRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const integrationPort =
  process.env["MAGPIE_INTEGRATION_PORT"] ?? String(await findAvailablePort());
const suiteEnvironment = {
  ...process.env,
  MAGPIE_INTEGRATION_PORT: integrationPort,
};

await acquireSuiteLock();

process.stdout.write(`Integration host port: ${integrationPort}\n`);

try {
  await reset();
  await run(["compose", "build", "magpie", "package-feed"]);
  await run(["compose", "--profile", "test", "build", "tests"]);
  await run(["compose", "up", "-d", "--wait"]);
  await run(["compose", "--profile", "test", "run", "--rm", "tests"]);
  await reset();
} catch (error) {
  try {
    await captureHostLogs();
  } catch (logError) {
    process.stderr.write(
      `Could not capture Magpie host logs: ${logError.message}\n`,
    );
  }
  process.stderr.write(
    "Kept the failed run for inspection; `pnpm down` discards it.\n",
  );
  throw error;
} finally {
  await rm(suiteLockPath, { force: true });
}

async function acquireSuiteLock() {
  for (let attempt = 0; attempt < 2; attempt += 1) {
    let lock;
    try {
      lock = await open(suiteLockPath, "wx");
    } catch (error) {
      if (error.code !== "EEXIST") {
        throw error;
      }

      const owner = Number.parseInt(
        await readFile(suiteLockPath, "utf8").catch(() => ""),
        10,
      );
      if (isRunning(owner)) {
        throw new Error(
          `Another integration run (process ${owner}) is in progress.`,
        );
      }

      await rm(suiteLockPath, { force: true });
      continue;
    }

    try {
      await lock.write(String(process.pid));
    } finally {
      await lock.close();
    }
    return;
  }

  throw new Error(`Could not take ${suiteLockPath}.`);
}

function isRunning(processId) {
  if (!Number.isInteger(processId) || processId <= 0) {
    return false;
  }

  try {
    process.kill(processId, 0);
    return true;
  } catch (error) {
    return error.code === "EPERM";
  }
}

async function captureHostLogs() {
  const outputDirectory = resolve(integrationRoot, "test-results");
  await mkdir(outputDirectory, { recursive: true });
  const output = await open(resolve(outputDirectory, "magpie.log"), "w");
  try {
    await run(
      ["compose", "logs", "--tail", "2000", "magpie"],
      ["ignore", output.fd, output.fd],
    );
  } finally {
    await output.close();
  }
}

function run(args, stdio = "inherit") {
  return new Promise((resolveRun, rejectRun) => {
    const child = spawn("docker", args, {
      cwd: integrationRoot,
      env: suiteEnvironment,
      shell: false,
      stdio,
      windowsHide: true,
    });
    child.once("error", rejectRun);
    child.once("close", (exitCode) => {
      if (exitCode === 0) {
        resolveRun();
        return;
      }
      rejectRun(
        new Error(`docker ${args.join(" ")} exited with ${exitCode ?? 1}.`),
      );
    });
  });
}

function findAvailablePort() {
  return new Promise((resolvePort, rejectPort) => {
    const server = createServer();
    server.once("error", rejectPort);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (!address || typeof address === "string") {
        server.close();
        rejectPort(new Error("Could not allocate an integration host port."));
        return;
      }

      server.close((error) => {
        if (error) {
          rejectPort(error);
          return;
        }

        resolvePort(address.port);
      });
    });
  });
}
