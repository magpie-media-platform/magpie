import { spawn } from "node:child_process";
import { mkdir, open } from "node:fs/promises";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { reset } from "./reset.mjs";

const integrationRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const integrationPort =
  process.env["MAGPIE_INTEGRATION_PORT"] ?? String(await findAvailablePort());
const suiteEnvironment = {
  ...process.env,
  MAGPIE_INTEGRATION_PORT: integrationPort,
};

process.stdout.write(`Integration host port: ${integrationPort}\n`);

try {
  await reset();
  await run(["compose", "build", "magpie"]);
  await run(["compose", "--profile", "test", "build", "tests"]);
  await run(["compose", "up", "-d", "--wait"]);
  await run(["compose", "--profile", "test", "run", "--rm", "tests"]);
} catch (error) {
  try {
    await captureHostLogs();
  } catch (logError) {
    process.stderr.write(
      `Could not capture Magpie host logs: ${logError.message}\n`,
    );
  }
  throw error;
} finally {
  await reset();
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
