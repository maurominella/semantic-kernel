# Mauro Minella repository for storing samples about<br/>
# `Semantic Kernel` samples

## Environment preparation

### 1. Install Git from its [WEB site](https://git-scm.com/downloads), choosing your operating system

### 2. Open a powershell or bash command prompt, making sure that git executable is in the path

### 3. ***CD*** into the base folder for your git repositories
If you do not have one, you may create a folder called `git_repos`

### 4. Use `git` to clone this repo locally
```git clone --branch my-feature-branch --single-branch https://github.com/maurominella/semantic-kernel.git```<br/>
**Note**: do **NOT** *CD* into this folder yet

### 5. Create a sub-folder of the base **`git_repos`** called **`config`** if it does not exist yet
Then, copy the file `credentials_my(template).env` of the cloned repo into the *config* folder:
- ```cp ./semantic-kernel/credentials_my\(template\).env ./config```

### 6. Update the variables in the credentials file `./config/credentials_my(template).env`, then save it as `./config/credentials_my.env`

### 7. ***CD*** into `semantic_kernel` folder of the cloned repository
```cd semantic-kernel```

### 8. Install Miniconda from its [WEB site](https://www.anaconda.com/docs/getting-started/miniconda/install), choosing your operating system

### 9. Open Miniconda bash / prompt, or make sure that conda executable is in the path

### 10. Environment provisioning for Semantic Kernel (`semantic_kernel`)

#### 10.1 Remove the pre-existing conda `semantic_kernel` environment (if exists)
```conda env remove -n semantic_kernel -y```

#### 10.2 Create new Conda Environment `semantic_kernel` with Python 3.13
```conda create -n semantic_kernel python=3.13 -y```

#### 10.3 Activate the `semantic_kernel` environment
```conda activate semantic_kernel```

#### 10.4 Install libraries and dependencies
```pip install -r requirements_semantic-kernel.txt```

#### 10.5 Remove `semantic_kernel` kernel (if exists)
```jupyter kernelspec uninstall semantic_kernel -y```

#### 10.6 Create `semantic_kernel` kernel 
```python -m ipykernel install --name semantic_kernel --display-name semantic_kernel --user```

#### 10.7 Check kernels list to make sure that `semantic_kernel` exists
```jupyter kernelspec list```
